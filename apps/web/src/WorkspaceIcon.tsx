import type { LibraryCategory } from "./types";

export type IconName =
  | LibraryCategory
  | "home"
  | "folder"
  | "tasks"
  | "search"
  | "list"
  | "grid"
  | "plus"
  | "chevron"
  | "arrow-left"
  | "refresh"
  | "close"
  | "info"
  | "check"
  | "menu"
  | "file"
  | "locate"
  | "logout";
const paths: Record<IconName, string> = {
  home: "m3 10 9-7 9 7v10H3Zm6 10v-7h6v7",
  folder: "M3 6h7l2 3h9v11H3Z",
  photos: "M3 7h5l2-3h4l2 3h5v13H3Zm13 6a4 4 0 1 1-8 0 4 4 0 0 1 8 0",
  images: "M3 3h18v18H3Zm0 13 5-5 4 4 3-3 6 6M8 7h.01",
  videos: "M3 4h18v16H3Zm6 4 7 4-7 4Z",
  music: "M9 18V5l11-2v13M9 7l11-2M9 18a3 2 0 1 1-3-2c2 0 3 1 3 2m11-2a3 2 0 1 1-3-2c2 0 3 1 3 2",
  projects: "m12 3 9 5v9l-9 5-9-5V8Zm0 10 9-5m-9 5L3 8m9 5v9",
  documents: "M5 3h10l4 4v14H5Zm9 0v6h5M8 13h8m-8 4h6",
  characters: "M16 7a4 4 0 1 1-8 0 4 4 0 0 1 8 0M4 21v-3a8 8 0 0 1 16 0v3",
  general: "M3 3h7v7H3Zm11 0h7v7h-7ZM3 14h7v7H3Zm11 0h7v7h-7Z",
  tasks: "M8 3h8v4H8ZM5 5H3v16h18V5h-2M7 12l2 2 3-3m2 1h3M7 18h10",
  search: "M17 10a7 7 0 1 1-14 0 7 7 0 0 1 14 0Zm-2 5 6 6",
  list: "M8 5h13M8 12h13M8 19h13M3 5h.01M3 12h.01M3 19h.01",
  grid: "M3 3h7v7H3Zm11 0h7v7h-7ZM3 14h7v7H3Zm11 0h7v7h-7Z",
  plus: "M12 5v14M5 12h14",
  chevron: "m9 5 7 7-7 7",
  "arrow-left": "m11 5-7 7 7 7M4 12h16",
  refresh: "M20 7v5h-5M4 17v-5h5M5 8a8 8 0 0 1 14-2l1 6M4 12l1 6a8 8 0 0 0 14-2",
  close: "m6 6 12 12M6 18 18 6",
  info: "M12 11v6m0-10h.01M22 12a10 10 0 1 1-20 0 10 10 0 0 1 20 0",
  check: "m5 12 4 4L19 6",
  menu: "M3 5h18M3 12h18M3 19h18",
  file: "M5 3h9l5 5v13H5Zm9 0v6h5",
  locate: "M12 3v4m0 10v4M3 12h4m10 0h4m-4-4a7 7 0 1 1-10 0 7 7 0 0 1 10 0",
  logout: "M10 4H4v16h6m5-14 6 6-6 6M9 12h12",
};

export function WorkspaceIcon({ name, className = "" }: { name: IconName; className?: string }) {
  return (
    <svg
      className={`workspace-icon ${className}`}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.7"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <path d={paths[name]} />
    </svg>
  );
}
