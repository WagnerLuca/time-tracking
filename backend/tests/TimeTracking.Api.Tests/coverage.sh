#!/usr/bin/env bash
# Runs the test suite with coverage (coverlet collector, see coverlet.runsettings), renders
# TestResults/coverage-report/ (index.html, SummaryGithub.md, Cobertura.xml) with ReportGenerator
# and fails below the thresholds. Extra arguments go to `dotnet test` (CI: --no-build -c Release ...).
# Local: `bash coverage.sh` from this folder (Docker needed for the Postgres tests).
set -euo pipefail
cd "$(dirname "$0")"

MIN_LINE=93     # % line coverage; measured 93.9 on 2026-10-07 — raise it when coverage grows
MIN_BRANCH=75   # % branch coverage; measured 75.9

rm -rf TestResults/coverage TestResults/coverage-report
dotnet test --settings coverlet.runsettings --collect:"XPlat Code Coverage" --results-directory TestResults/coverage "$@"

dotnet tool restore >/dev/null
dotnet reportgenerator -reports:"TestResults/coverage/*/coverage.cobertura.xml" -targetdir:TestResults/coverage-report \
  -reporttypes:"Html;MarkdownSummaryGithub;TextSummary;Cobertura" -verbosity:Warning

if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then cat TestResults/coverage-report/SummaryGithub.md >> "$GITHUB_STEP_SUMMARY"; fi

summary=TestResults/coverage-report/Summary.txt
line=$(awk '/^ *Line coverage:/ { sub("%", "", $3); print $3 }' "$summary")
branch=$(awk '/^ *Branch coverage:/ { sub("%", "", $3); print $3 }' "$summary")
echo "Coverage: lines ${line}% (min ${MIN_LINE}%), branches ${branch}% (min ${MIN_BRANCH}%)"
awk -v l="$line" -v b="$branch" -v ml="$MIN_LINE" -v mb="$MIN_BRANCH" 'BEGIN { exit !(l >= ml && b >= mb) }' \
  || { echo "::error::Backend coverage below threshold (lines ${line}% < ${MIN_LINE}% or branches ${branch}% < ${MIN_BRANCH}%)"; exit 1; }
