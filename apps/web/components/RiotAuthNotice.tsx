"use client";

import { useEffect, useState } from "react";

const ERROR_MESSAGES: Record<string, string> = {
  unavailable: "Riot sign-in is not available right now.",
  cancelled: "Riot sign-in was cancelled.",
  "invalid-state": "That Riot sign-in session expired. Please try again.",
  "session-expired": "Your Transcendence session expired. Sign in again before linking Riot.",
  "start-failed": "Couldn't start Riot sign-in. Try again.",
  "login-400": "Couldn't verify Riot sign-in. Try again.",
  "login-401": "Riot could not verify that sign-in. Please try again.",
  "login-503": "Riot sign-in is unavailable."
};

export function RiotAuthNotice() {
  const [message, setMessage] = useState<string | null>(null);

  useEffect(() => {
    const value = new URLSearchParams(window.location.search).get("riotError");
    if (value) setMessage(ERROR_MESSAGES[value] ?? "Riot sign-in could not be completed.");
  }, []);

  return message ? (
    <p className="field-error type-ui mt-4" role="alert">
      {message}
    </p>
  ) : null;
}
