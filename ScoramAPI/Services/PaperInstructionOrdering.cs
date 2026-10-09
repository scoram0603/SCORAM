using ScoramAPI.Models;

namespace ScoramAPI.Services
{
    public static class PaperInstructionOrdering
    {
        public const int MaxInstructionsPerPaper = 50;

        // Re-numbers the paper's ACTIVE instructions 0..n-1 in the requested order. Throws ArgumentException
        // (user-facing) unless orderedIds is exactly the set of active instructions -- so a stale screen can
        // never silently drop or duplicate an instruction. Archived rows keep their old number.
        public static void Apply(IList<PaperInstruction> active, IList<Guid> orderedIds)
        {
            if (orderedIds == null) throw new ArgumentException("Send the full list of instructions in the new order.");
            if (orderedIds.Distinct().Count() != orderedIds.Count)
                throw new ArgumentException("The new order contains the same instruction twice.");
            var activeIds = active.Select(a => a.Id).ToHashSet();
            if (orderedIds.Count != activeIds.Count || !orderedIds.All(activeIds.Contains))
                throw new ArgumentException("The instruction list changed. Refresh the page and try again.");

            var byId = active.ToDictionary(a => a.Id);
            for (var i = 0; i < orderedIds.Count; i++) byId[orderedIds[i]].DisplayOrder = i;
        }
    }
}
