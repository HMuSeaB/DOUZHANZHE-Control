import { useEffect, useState } from "react";

export function useStall(active, delayMs = 4000) {
  const [stalled, setStalled] = useState(false);
  useEffect(() => {
    if (!active) {
      if (!stalled) return undefined;
      const reset = setTimeout(() => setStalled(false), 0);
      return () => clearTimeout(reset);
    }
    if (stalled) return undefined;
    const timer = setTimeout(() => setStalled(true), delayMs);
    return () => clearTimeout(timer);
  }, [active, stalled, delayMs]);
  return stalled;
}
