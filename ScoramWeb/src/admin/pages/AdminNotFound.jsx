import { Link } from "react-router-dom";
import { Compass } from "lucide-react";

export default function AdminNotFound() {
  return (
    <div className="flex min-h-[70vh] flex-col items-center justify-center gap-3 px-8 text-center">
      <span className="flex h-14 w-14 items-center justify-center rounded-full bg-primary-50 text-primary-600">
        <Compass className="h-7 w-7" strokeWidth={2} />
      </span>
      <p className="text-lg font-bold text-ink-900">Page not found</p>
      <p className="max-w-sm text-sm text-ink-400">This admin page doesn't exist, or may have moved.</p>
      <Link
        to="/admin"
        className="mt-2 rounded-xl2 bg-primary-600 px-5 py-2.5 text-sm font-semibold text-white transition-colors hover:bg-primary-700"
      >
        Back to Dashboard
      </Link>
    </div>
  );
}
