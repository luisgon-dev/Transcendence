"""Fail on any NuGet advisory or an incomplete machine-readable audit."""
import json
import sys

report = json.load(open(sys.argv[1], encoding="utf-8"))
if report.get("version") != 1 or not report.get("projects") or report.get("errors"):
    raise SystemExit("NuGet audit did not return a complete v1 report")
findings = []
for project in report["projects"]:
    for framework in project.get("frameworks", []):
        for package in framework.get("topLevelPackages", []) + framework.get("transitivePackages", []):
            for vulnerability in package.get("vulnerabilities", []):
                findings.append(f"{package['id']} {package['resolvedVersion']}: {vulnerability['severity']} {vulnerability['advisoryurl']}")
if findings:
    raise SystemExit("\n".join(sorted(set(findings))))
print("NuGet audit: no known vulnerabilities")
