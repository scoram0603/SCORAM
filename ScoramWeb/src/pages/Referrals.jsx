import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { ArrowLeft, Loader2, Copy, Check, Share2, Gift, Users } from "lucide-react";
import { getMyReferrals } from "../api/referrals";
import { timeAgo } from "../utils/format";

export default function Referrals() {
  const navigate = useNavigate();
  const [data, setData] = useState(null);
  const [status, setStatus] = useState("loading");
  const [copied, setCopied] = useState(false);

  useEffect(() => {
    getMyReferrals()
      .then((res) => {
        setData(res);
        setStatus("success");
      })
      .catch(() => setStatus("error"));
  }, []);

  async function handleCopy() {
    try {
      await navigator.clipboard.writeText(data.referralCode);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      // Clipboard API unavailable (older browser / no permission) -- the code is still
      // right there on screen for the student to copy manually.
    }
  }

  async function handleShare() {
    if (navigator.share) {
      try {
        await navigator.share({ text: data.shareText });
      } catch {
        // User backed out of the native share sheet -- nothing to do.
      }
    } else {
      handleCopy();
    }
  }

  return (
    <div className="px-4 pb-10 pt-4 sm:px-6 lg:px-8 lg:pt-6">
      <button
        type="button"
        onClick={() => navigate("/profile")}
        className="flex items-center gap-1.5 text-sm font-semibold text-secondary-500"
      >
        <ArrowLeft className="h-4 w-4" strokeWidth={2.5} />
        Profile
      </button>

      <h1 className="mt-3 text-xl font-extrabold text-ink-900 sm:text-2xl">Refer & Earn</h1>
      <p className="mt-1 text-sm text-ink-400">Invite friends and earn bonus XP + extra Mock Test attempts.</p>

      {status === "loading" && (
        <div className="flex justify-center py-16 text-ink-400">
          <Loader2 className="h-6 w-6 animate-spin" strokeWidth={2.25} />
        </div>
      )}

      {status === "error" && (
        <p className="mt-4 rounded-xl2 border border-red-100 bg-red-50 p-4 text-sm text-red-600">
          Couldn't load your referral info right now.
        </p>
      )}

      {status === "success" && (
        <>
          <div className="mt-5 rounded-xl2 border border-primary-100 bg-primary-50/60 p-5 text-center">
            <p className="text-xs font-semibold uppercase tracking-wide text-ink-400">Your referral code</p>
            <p className="mt-1 text-2xl font-extrabold tracking-widest text-primary-600">{data.referralCode}</p>

            <div className="mt-4 flex justify-center gap-2">
              <button
                type="button"
                onClick={handleCopy}
                className="flex items-center gap-1.5 rounded-lg bg-white px-4 py-2.5 text-sm font-semibold text-ink-600 shadow-card transition-colors hover:bg-primary-50"
              >
                {copied ? <Check className="h-4 w-4 text-mint-500" strokeWidth={2.5} /> : <Copy className="h-4 w-4" strokeWidth={2.25} />}
                {copied ? "Copied" : "Copy Code"}
              </button>
              <button
                type="button"
                onClick={handleShare}
                className="flex items-center gap-1.5 rounded-lg bg-primary-600 px-4 py-2.5 text-sm font-semibold text-white transition-colors hover:bg-primary-700"
              >
                <Share2 className="h-4 w-4" strokeWidth={2.25} />
                Share
              </button>
            </div>
          </div>

          <div className="mt-4 grid grid-cols-3 gap-3">
            <div className="rounded-xl2 border border-primary-100 bg-white p-3.5 text-center shadow-card">
              <p className="text-xl font-extrabold text-ink-900">{data.totalJoins}</p>
              <p className="mt-0.5 text-[11px] text-ink-400">Friends joined</p>
            </div>
            <div className="rounded-xl2 border border-primary-100 bg-white p-3.5 text-center shadow-card">
              <p className="text-xl font-extrabold text-ink-900">{data.totalXpEarned}</p>
              <p className="mt-0.5 text-[11px] text-ink-400">XP earned</p>
            </div>
            <div className="rounded-xl2 border border-primary-100 bg-white p-3.5 text-center shadow-card">
              <p className="text-xl font-extrabold text-ink-900">{data.bonusMockAttempts}</p>
              <p className="mt-0.5 text-[11px] text-ink-400">Bonus attempts</p>
            </div>
          </div>

          <h2 className="mt-6 flex items-center gap-1.5 text-sm font-bold text-ink-900">
            <Users className="h-4 w-4" strokeWidth={2.25} />
            Your referrals
          </h2>
          <div className="mt-3 flex flex-col gap-2">
            {data.referrals.length === 0 && (
              <p className="rounded-xl2 border border-primary-100 bg-white p-4 text-sm text-ink-400">
                No one has joined with your code yet — share it to start earning.
              </p>
            )}
            {data.referrals.map((r, i) => (
              <div key={i} className="flex items-center gap-3 rounded-xl2 border border-primary-100 bg-white p-3.5 shadow-card">
                <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-mint-50 text-mint-500">
                  <Gift className="h-4 w-4" strokeWidth={2.25} />
                </span>
                <div className="min-w-0 flex-1">
                  <p className="truncate text-sm font-bold text-ink-900">{r.referredFullName}</p>
                  <p className="text-xs text-ink-400">{r.joinedAt ? `Joined ${timeAgo(r.joinedAt)} ago` : "Joined"}</p>
                </div>
              </div>
            ))}
          </div>
        </>
      )}
    </div>
  );
}
