using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ServerCore.DataModel;
using ServerCore.ModelBases;

namespace ServerCore.Pages.Events
{
    public class StandingsModel : EventSpecificPageModel
    {
        public List<TeamStats> Teams { get; private set; }

        public SortOrder? Sort { get; set; }

        private const SortOrder DefaultSort = SortOrder.RankAscending;

        public StandingsModel(PuzzleServerContext serverContext, UserManager<IdentityUser> userManager) : base(serverContext, userManager)
        {
        }

        public async Task OnGetAsync(SortOrder? sort)
        {
            Sort = sort;

            List<TeamStats> teams = await PuzzleStateHelper.GetSparseQuery(_context, this.Event, null, null)
                .Where(s => s.SolvedTime != null && s.Puzzle.IsPuzzle)
                .GroupBy(state => state.Team)
                .Select(g => new TeamStats
                {
                    Team = g.Key,
                    SolveCount = g.Count(),
                    Score = g.Sum(s => s.Puzzle.SolveValue),
                    FinalMetaSolveTime = g.Where(s => s.Puzzle.IsCheatCode).Any() ?
                            DateTime.MaxValue :
                            (g.Where(s => s.Puzzle.IsFinalPuzzle).Select(s => s.SolvedTime).FirstOrDefault() ?? DateTime.MaxValue)
                })
                .OrderBy(t => t.FinalMetaSolveTime).ThenByDescending(t => t.Score).ThenBy(t => t.Team.Name)
                .ToListAsync();

            if (this.Event.USE_ALTERNATE_FINAL_META_SCORING)
            {
                int adjustment = 0;
                for (int i = 0; i < teams.Count; i++)
                {
                    var data = teams[i];
                    if (data.FinalMetaSolveTime != DateTime.MaxValue)
                    {
                        data.Score -= adjustment;
                        adjustment = Math.Min(adjustment + this.Event.FINAL_META_DELTA, this.Event.MAX_FINAL_META_ADJUSTMENT);
                    }
                }
                teams.Sort((a, b) => {
                    // List.Sort is unstable so we have to explicitly specify the secondary keys.
                    int comparison = a.Score.CompareTo(b.Score);
                    if (comparison == 0)
                    {
                        comparison = a.FinalMetaSolveTime.CompareTo(b.FinalMetaSolveTime);
                        if (comparison == 0)
                        {
                            comparison = a.Team.Name.CompareTo(b.Team.Name);
                        }
                    }
                    return comparison;
                });
            }

            TeamStats prevStats = null;
            for (int i = 0; i < teams.Count; i++)
            {
                TeamStats stats = teams[i];

                // This code assigns the same rank to two teams with the same score and the same final meta solve time.
                if (prevStats == null || stats.FinalMetaSolveTime != prevStats.FinalMetaSolveTime || stats.Score != prevStats.Score)
                {
                    stats.Rank = i + 1;
                }
                else
                {
                    stats.Rank = prevStats.Rank;
                }

                prevStats = stats;
            }

            switch (sort)
            {
                case SortOrder.RankAscending:
                    break;
                case SortOrder.RankDescending:
                    teams.Reverse();
                    break;
                case SortOrder.NameAscending:
                    teams.Sort((a, b) => a.Team.Name.CompareTo(b.Team.Name));
                    break;
                case SortOrder.NameDescending:
                    teams.Sort((a, b) => -a.Team.Name.CompareTo(b.Team.Name));
                    break;
                case SortOrder.PuzzlesAscending:
                    SortTeams(teams, (a, b) => a.SolveCount.CompareTo(b.SolveCount));
                    break;
                case SortOrder.PuzzlesDescending:
                    SortTeams(teams, (a, b) => -a.SolveCount.CompareTo(b.SolveCount));
                    break;
                case SortOrder.ScoreAscending:
                    SortTeams(teams, (a, b) => a.Score.CompareTo(b.Score));
                    break;
                case SortOrder.ScoreDescending:
                    SortTeams(teams, (a, b) => -a.Score.CompareTo(b.Score));
                    break;
                case SortOrder.HintsUsedAscending:
                    SortTeams(teams, (a, b) => a.Score.CompareTo(b.Team.HintCoinsUsed));
                    break;
                case SortOrder.HintsUsedDescending:
                    SortTeams(teams, (a, b) => -a.Score.CompareTo(b.Team.HintCoinsUsed));
                    break;
            }

            this.Teams = teams;
        }

        public void SortTeams(List<TeamStats> teams, Comparison<TeamStats> comparator) {
            // List.Sort is unstable so we have to explicitly specify the secondary keys
            // to get a stable result. Breaks ties by rank and then by name. Don't need 
            // to consider score or final meta solve time because ranks being equal implies
            // those are equal too.
            teams.Sort((a, b) =>
            {
                int comparison = comparator(a, b);
                if (comparison == 0)
                {
                    comparison = a.Rank.CompareTo(b.Rank);
                    if (comparison == 0)
                    {
                        comparison = a.Team.Name.CompareTo(b.Team.Name);
                    }
                }
                return comparison;
            });
        }

        public SortOrder? SortForColumnLink(SortOrder ascendingSort, SortOrder descendingSort)
        {
            SortOrder result = ascendingSort;

            if (result == (this.Sort ?? DefaultSort))
            {
                result = descendingSort;
            }

            if (result == DefaultSort)
            {
                return null;
            }

            return result;
        }

        public class TeamStats
        {
            public Team Team;
            public int SolveCount;
            public int Score;
            public int Rank;
            public DateTime FinalMetaSolveTime = DateTime.MaxValue;
        }

        public enum SortOrder
        {
            RankAscending,
            RankDescending,
            NameAscending,
            NameDescending,
            PuzzlesAscending,
            PuzzlesDescending,
            ScoreAscending,
            ScoreDescending,
            HintsUsedAscending,
            HintsUsedDescending
        }
    }
}
