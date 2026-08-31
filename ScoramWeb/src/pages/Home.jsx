import { useState } from "react";
import { useNavigate } from "react-router-dom";
import TopBar from "../components/layout/TopBar";
import SearchBar from "../components/home/SearchBar";
import HeroBanner from "../components/home/HeroBanner";
import QuickAccess from "../components/home/QuickAccess";
import StreakXPCard from "../components/home/StreakXPCard";
import PreparingFor from "../components/home/PreparingFor";
import PopularExams from "../components/home/PopularExams";
import TodaysChallenge from "../components/home/TodaysChallenge";
import TopDiscussions from "../components/home/TopDiscussions";
import RecentTests from "../components/home/RecentTests";
import { useAuth } from "../context/AuthContext";

export default function Home() {
  const [query, setQuery] = useState("");
  const { isAuthenticated, user } = useAuth();
  const navigate = useNavigate();

  function handleSearchSubmit(keyword) {
    const trimmed = keyword.trim();
    navigate(trimmed ? `/search?q=${encodeURIComponent(trimmed)}` : "/search");
  }

  return (
    <div className="pb-4 lg:pb-10">
      <TopBar
        query={query}
        onQueryChange={setQuery}
        onSearchSubmit={handleSearchSubmit}
        onFiltersClick={() => navigate("/search")}
        isAuthenticated={isAuthenticated}
        user={user}
      />
      <SearchBar value={query} onChange={setQuery} onSubmit={handleSearchSubmit} onFilterClick={() => navigate("/search")} />

      <div className="mx-auto w-full lg:max-w-6xl xl:max-w-7xl">
        <HeroBanner />
        <QuickAccess />
        <StreakXPCard />
        <PreparingFor />

        {/* Main content grid: 60/40 split from lg up */}
        <div className="lg:grid lg:grid-cols-5 lg:gap-6 lg:px-8">
          <div className="lg:col-span-3">
            <TodaysChallenge />
            <PopularExams />
          </div>
          <div className="lg:col-span-2">
            <TopDiscussions />
            <RecentTests />
          </div>
        </div>
      </div>
    </div>
  );
}
