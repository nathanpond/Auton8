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