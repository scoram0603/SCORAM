import { useState } from "react";
import { Link } from "react-router-dom";
import { Loader2, Quote } from "lucide-react";
import ScrollReveal from "./ScrollReveal";
import { StarDisplay } from "../reviews/StarRating";
import { usePublicReviews } from "../../hooks/usePublicReviews";
import { formatReviewDate, reviewInitials } from "../../utils/reviews";

// "Students Preparing with SCORAM" -- LIVE student reviews from GET /api/public/reviews.
// The API only returns reviews a moderator approved AND that are rated 3-5 stars (enforced server-side; the hook
// filters again as a presentation safeguard). There is deliberately NO fallback testimonial: with no eligible
// reviews the section shows an honest empty state, and if the request fails it says so quietly -- the rest of the
// landing page is never affected.
const CLAMP_AT = 280; // characters; longer reviews get a "Read more" toggle so cards stay a sensible height

function ReviewCard({ review, index }) {
  const [expanded, setExpanded] = useState(false);
  const long = review.text.length > CLAMP_AT;
  return (
    <ScrollReveal delay={(index % 3) * 80}>
      <figure className="flex h-full flex-col rounded-2xl border border-primary-100 bg-white p-6 shadow-card">
        <div className="flex items-center justify-between gap-3">
          <StarDisplay rating={review.rating} />
          <Quote className="h-6 w-6 shrink-0 text-primary-100" strokeWidth={2} fill="currentColor" aria-hidden="true" />
        </div>
        {/* Long reviews are clamped for layout, never cut in the data: "Read more" reveals the full text. */}
        <blockquote className={`mt-3 flex-1 whitespace-pre-line break-words text-[15px] leading-relaxed text-ink-600 ${long && !expanded ? "line-clamp-[6]" : ""}`}>
          {review.text}
        </blockquote>
        {long && (
          <button type="button" onClick={() => setExpanded((v) => !v)} aria-expanded={expanded} className="mt-2 self-start text-sm font-semibold text-secondary-500 hover:text-secondary-600">
            {expanded ? "Show less" : "Read more"}
          </button>
        )}
        <figcaption className="mt-5 flex items-center gap-3 border-t border-primary-50 pt-4">
          <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-primary-50 text-sm font-bold text-primary-600" aria-hidden="true">
            {reviewInitials(review.displayName)}
          </span>
          <span className="min-w-0">
            <span className="block truncate text-sm font-bold text-ink-900">{review.displayName}</span>
            <span className="block text-xs text-ink-400">{formatReviewDate(review.createdAt)}</span>
          </span>
        </figcaption>
      </figure>
    </ScrollReveal>
  );
}

function SkeletonCard() {
  return (
    <div className="h-52 animate-pulse rounded-2xl border border-primary-100 bg-white p-6" aria-hidden="true">
      <div className="h-4 w-24 rounded bg-primary-50" />
      <div className="mt-4 space-y-2">
        <div className="h-3 rounded bg-primary-50" />
        <div className="h-3 rounded bg-primary-50" />
        <div className="h-3 w-2/3 rounded bg-primary-50" />
      </div>
    </div>
  );
}

export default function TestimonialsSection() {
  const { status, items, loadingMore, moreError, loadMore, retry, hasMore } = usePublicReviews();

  return (
    <section className="bg-surface py-20 lg:py-28" aria-labelledby="landing-reviews-title">
      <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
        <ScrollReveal className="mx-auto max-w-2xl text-center">
          <p className="text-sm font-semibold uppercase tracking-wide text-accent-500">Student Voices</p>
          <h2 id="landing-reviews-title" className="mt-2 text-3xl font-extrabold tracking-tight text-ink-900 sm:text-4xl">
            Students Preparing with SCORAM
          </h2>
        </ScrollReveal>

        {status === "loading" && (
          <div className="mt-12 grid grid-cols-1 gap-5 sm:grid-cols-2 lg:grid-cols-3 lg:gap-6" role="status" aria-label="Loading student reviews">
            {[0, 1, 2].map((i) => <SkeletonCard key={i} />)}
          </div>
        )}

        {status === "error" && (
          <div className="mx-auto mt-10 max-w-md rounded-2xl border border-primary-100 bg-white p-6 text-center">
            <p className="text-sm text-ink-600">Student reviews couldn't be loaded right now.</p>
            <button type="button" onClick={retry} className="mt-3 text-sm font-semibold text-secondary-500 hover:text-secondary-600">
              Try again
            </button>
          </div>
        )}

        {status === "ready" && items.length === 0 && (
          <div className="mx-auto mt-10 max-w-md rounded-2xl border border-primary-100 bg-white p-6 text-center">
            <p className="text-sm text-ink-600">No student reviews yet. Reviews from SCORAM students will appear here.</p>
            <Link to="/rate-scoram" className="mt-3 inline-block text-sm font-semibold text-secondary-500 hover:text-secondary-600">
              Share your experience
            </Link>
          </div>
        )}

        {items.length > 0 && (
          <>
            <div className="mt-12 grid grid-cols-1 gap-5 sm:grid-cols-2 lg:grid-cols-3 lg:gap-6">
              {items.map((review, i) => <ReviewCard key={review.id} review={review} index={i} />)}
            </div>

            <div className="mt-8 flex flex-col items-center gap-3 text-center">
              {hasMore && (
                <button
                  type="button"
                  onClick={loadMore}
                  disabled={loadingMore}
                  className="inline-flex items-center gap-2 rounded-xl2 border border-primary-100 bg-white px-5 py-2.5 text-sm font-semibold text-primary-600 hover:bg-primary-50 disabled:opacity-60"
                >
                  {loadingMore && <Loader2 className="h-4 w-4 animate-spin" strokeWidth={2.5} />}
                  Show more reviews
                </button>
              )}
              {moreError && <p className="text-xs text-red-600" role="alert">Couldn't load more reviews. Please try again.</p>}
              <Link to="/rate-scoram" className="text-sm font-semibold text-secondary-500 hover:text-secondary-600">
                Write a review
              </Link>
            </div>
          </>
        )}
      </div>
    </section>
  );
}
