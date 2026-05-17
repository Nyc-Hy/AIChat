# Dotnet Project Helper

Use this skill when inspecting, building, testing, or diagnosing a .NET project.

Workflow:

1. Prefer `dotnet build` before deeper debugging if the user reports compile errors.
2. Prefer targeted `dotnet test` commands when the failing project or test class is known.
3. Read `.csproj`, `Directory.Build.props`, and `global.json` before changing SDK, package, or build behavior.
4. Keep changes scoped to the failing project unless the solution-level configuration is clearly involved.
5. Report the exact command and result after verification.
