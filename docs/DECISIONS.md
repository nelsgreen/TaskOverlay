# TaskOverlay Product Decisions

This document records current product and implementation decisions. Revisit a
decision here when the product direction changes, then update the relevant
roadmap or backlog item.

## Current Decisions

- WPF v2 is the main product.
- Go v1 is legacy.
- The correct executable for v2 is `TaskOverlay.V2.exe`.
- The correct development artifact is `TaskOverlayV2_WPF_FrameworkDependent`.
- Use REMIND, not DUE.
- Use FOCUS, not IN WORK.
- Use Focus, not Acknowledge.
- REMIND is not Deadline.
- Deadline is not REMIND.
- Overlay is the attention layer.
- Tree is the master structure.
- Handle anchor is the source of truth and must not be derived from panel position.
- Keep PRs bounded, but avoid micro-PRs for tiny UI changes.
