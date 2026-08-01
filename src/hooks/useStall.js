import { useEffect, useState } from "react";

export function useStall(active, delayMs = 4000) {
  const [stalled, setStalled] = useState(false);
  useEffect(() => {
    if (!active || stalled) return undefined;
    const t = setTimeout(() => setStalled(true), delayMs);
    return () => clearTimeout(t);
  }, [active, stalled, delayMs]);
  return stalled;
}
