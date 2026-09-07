import type { AccessLevel, Entry, LibraryCategory } from "./types";

export const libraryCategories: readonly LibraryCategory[] = [
  "photos",
  "images",
  "videos",
  "music",
  "projects",
  "documents",
  "characters",
  "general",
];
export const categoryLabels: Record<LibraryCategory, string> = {
  photos: "照片",
  images: "图片",
  videos: "视频",
  music: "音乐",
  projects: "工程",
  documents: "文档与阅读",
  characters: "角色",
  general: "通用",
};

export function isLibraryCategory(value: string): value is LibraryCategory {
  return libraryCategories.some((category) => category === value);
}

export function accessLabel(value: AccessLevel): string {
  return { read_only: "只读", read_write: "可读写", organize: "可整理", library_administrator: "资源库管理员" }[value];
}

export function parentPath(path: string): string {
  const index = path.lastIndexOf("/");
  return index < 0 ? "" : path.slice(0, index);
}

export function entryType(entry: Entry): string {
  if (entry.kind === "directory") return "文件夹";
  if (entry.kind === "reparse_directory") return "链接文件夹";
  if (entry.kind === "reparse_file") return "链接文件";
  const dot = entry.name.lastIndexOf(".");
  return dot > 0 && dot < entry.name.length - 1 ? `${entry.name.slice(dot + 1).toUpperCase()} 文件` : "文件";
}

export function formatBytes(value: string | null): string {
  if (value === null) return "—";
  const bytes = Number(value);
  if (!Number.isFinite(bytes) || bytes < 0) return "—";
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 ** 2) return `${(bytes / 1024).toFixed(1)} KB`;
  if (bytes < 1024 ** 3) return `${(bytes / 1024 ** 2).toFixed(1)} MB`;
  return `${(bytes / 1024 ** 3).toFixed(1)} GB`;
}

export function formatDate(value: string): string {
  const date = new Date(value);
  return Number.isFinite(date.getTime()) ? date.toLocaleString("zh-CN", { hour12: false }) : "—";
}
