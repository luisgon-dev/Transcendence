import sys

from .pipeline import RunOutcome, run

# The exit code is the only thing a oneshot scheduler sees, so map the outcome onto it rather than
# always exiting 0. `idle` and `completed` are both successful ticks; a generation that failed its
# gates or blew up must surface as a unit failure, visible without reading the database.
EXIT_CODES = {
    RunOutcome.IDLE: 0,
    RunOutcome.COMPLETED: 0,
    RunOutcome.FAILED: 1,
}

if __name__ == "__main__":
    sys.exit(EXIT_CODES[run()])
