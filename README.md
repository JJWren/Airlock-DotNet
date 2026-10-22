# Airlock (.NET)

Airlock is a gateway that checks third-party software before it is admitted into a controlled environment. It builds
an SBOM of a directory or of uploaded artifacts with [Syft](https://github.com/anchore/syft) and correlates every
package with the NIST National Vulnerability Database. This repository rebuilds the original Python and React Airlock
on .NET 10 with a Blazor UI.

**Status:** under construction — unit U1 (Domain) of six. The full README arrives with U6.

- Vocabulary: [GLOSSARY.md](GLOSSARY.md)
- Decisions: [docs/adr/](docs/adr/)
- Requirements, design and progress: [aidlc-docs/](aidlc-docs/) — start with
  [aidlc-state.md](aidlc-docs/aidlc-state.md)

## Build and test

Requires the .NET 10 SDK (pinned in `global.json`).

```bash
dotnet build
dotnet test
```

## License

MIT — see [LICENSE](LICENSE).
