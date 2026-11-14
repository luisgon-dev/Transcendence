"use client";
import { useEffect } from "react";
import { recordJourney } from "@/lib/journeyMetrics";

export function JourneyReporter() {
  useEffect(() => {
    try {
      if (sessionStorage.getItem("trn:session-counted")) return;
      recordJourney(localStorage.getItem("trn:visited") ? "return_session" : "new_session");
      localStorage.setItem("trn:visited", "1");
      sessionStorage.setItem("trn:session-counted", "1");
    } catch { /* Metrics are optional when browser storage is disabled. */ }
  }, []);
  return null;
}
