// FEED REDESIGN / PREMIUM UI -- skeleton loader shown while the first page of results is loading,
// instead of a blank screen (brief section 15). Shape mirrors QuestionBankFeedCard so there's no
// layout jump when real content replaces it.
export default function QuestionBankFeedCardSkeleton() {
  return (
    <div className="animate-pulse rounded-xl2 border border-primary-100 bg-white p-4 shadow-card sm:p-5">
      <div className="flex gap-1.5">
        <div className="h-5 w-20 rounded-md bg-primary-50" />
        <div className="h-5 w-24 rounded-md bg-primary-50" />
      </div>
      <div className="mt-3 h-4 w-full rounded bg-primary-50" />
      <div className="mt-2 h-4 w-4/5 rounded bg-primary-50" />
      <div className="mt-4 grid grid-cols-1 gap-2 sm:grid-cols-2">
        <div className="h-10 rounded-lg bg-primary-50" />
        <div className="h-10 rounded-lg bg-primary-50" />
        <div className="h-10 rounded-lg bg-primary-50" />
        <div className="h-10 rounded-lg bg-primary-50" />
      </div>
      <div className="mt-4 flex gap-2 border-t border-primary-50 pt-3">
        <div className="h-7 w-24 rounded-lg bg-primary-50" />
        <div className="h-7 w-20 rounded-lg bg-primary-50" />
        <div className="h-7 w-16 rounded-lg bg-primary-50" />
      </div>
    </div>
  );
}
