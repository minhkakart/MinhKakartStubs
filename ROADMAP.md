# DiDecoration Roadmap

## Stage 1 — Core stability
**Goal:** make the current API reliable and predictable.

### Work items
- Add tests for:
  - `RegisterServices`
  - `RegisterHostedServices`
  - `RegisterHttpClients`
  - `RegisterOptions`
- Add fail-fast validation for:
  - invalid service mappings
  - invalid hosted service `ServiceType`
  - invalid HTTP client base URLs
  - invalid interceptor types
- Improve XML docs and usage examples
- Document registration-order rules

### Outcome
- Safer startup behavior
- Fewer runtime surprises
- Easier adoption for users

## Stage 2 — Feature growth
**Goal:** make the package more flexible for real-world usage.

### Work items
- Add assembly scanning filters
  - namespace filter
  - predicate filter
  - include/exclude internal types
- Add open generic service support
- Expand `HttpClientServiceAttribute`
  - default headers
  - client name override
  - handler customization
- Add one convenience registration method for all decorators

### Outcome
- Less startup boilerplate
- Better support for larger apps
- More practical attribute-driven registration

## Stage 3 — Developer experience
**Goal:** help users catch mistakes earlier and understand the package faster.

### Work items
- Add aggregated diagnostics
- Add analyzer support for invalid attribute usage
- Improve README and advanced examples
- Add common-pitfall guidance

### Outcome
- Better IDE feedback
- Easier debugging
- Faster onboarding

## Stage 4 — Performance and scale
**Goal:** reduce reflection overhead and improve startup time.

### Work items
- Add source generator support
  - separate `DiDecoration.Generators` package
  - emit reflection-free registration helpers into consumer apps
  - keep runtime scanning as the default fallback path
- Precompute registration metadata
- Reduce runtime scanning work

### Outcome
- Faster startup
- More compile-time safety
- Better fit for large applications

## Stage 5 — Hardening and polish
**Goal:** make the package more production-ready.

### Work items
- Add lifetime safety checks
- Improve keyed-service support
- Add a sample project
- Add versioned release notes

### Outcome
- Fewer DI mistakes
- Better documentation
- Stronger long-term maintainability

## Suggested release plan

### v1.1
- Tests
- Validation
- Docs
- README examples

### v1.2
- Scan filters
- Open generics
- HTTP client enhancements
- Convenience aggregate registration

### v1.3
- Diagnostics helpers
- Analyzer support
- Expanded examples

### v2.0
- Source generator
- Performance-focused changes
- Broader ecosystem polish

## Recommended order
1. Tests
2. Validation
3. README/examples
4. Scan filters
5. Open generics
6. Analyzer/source generator

