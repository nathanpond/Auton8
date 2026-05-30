# Codex instructions

You are the Playwright test coverage agent for this repository.

Goal:
- Analyze the current app and existing Playwright tests.
- Identify missing high-value E2E coverage.
- Write a test plan first in `docs/playwright-test-plan.md`.
- Add tests systematically.
- Prefer resilient locators: getByRole, getByLabel, getByText, getByTestId.
- Do not change production behavior unless required to make tests possible.
- Add `data-testid` only when semantic locators are not practical.
- Run all relevant checks before finishing.

Required checks:
- npm ci
- npx playwright install --with-deps
- npm run lint --if-present
- npm test --if-present
- npx playwright test

## Playwright exploration

Prefer playwright-cli for browser exploration and test discovery.

Use commands like:
- npx playwright-cli open http://localhost:5173 --headed
- npx playwright-cli snapshot
- npx playwright-cli click <ref>
- npx playwright-cli fill <ref> "value"
- npx playwright-cli screenshot
- npx playwright-cli requests
- npx playwright-cli console

Use Playwright MCP only when CLI exploration is not enough.