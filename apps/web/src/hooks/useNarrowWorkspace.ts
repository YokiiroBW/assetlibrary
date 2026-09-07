import { useEffect, useState } from "react";

export function useNarrowWorkspace() {
  const [narrow, setNarrow] = useState(() => window.matchMedia("(max-width: 1199px)").matches);
  useEffect(() => {
    const media = window.matchMedia("(max-width: 1199px)");
    const changed = () => setNarrow(media.matches);
    media.addEventListener("change", changed);
    return () => media.removeEventListener("change", changed);
  }, []);
  return narrow;
}
