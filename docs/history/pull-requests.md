# Pull request archive

> Archived from `nathanpond/AutoNate` (later `Auton8`) before the repository was
> migrated on 2026-09-02. GitHub cannot transfer pull requests between
> repositories, and the pre-migration repository is private permanently because
> its git history contains a commercially-licensed theme. This file exists so
> the reasoning behind the work survives in the open.
>
> **Numbers here are pre-migration.** A `#N` in a commit message written before
> 2026-09-02 refers to this register, not to the current one.

74 pull requests.

---

## archived-1 — smartdocs

`MERGED (merged 2026-05-30)` · nathanpond · opened 2026-05-30 · `smartdocs` → `master`

_No description._

---

## archived-2 — [codex] expand Playwright E2E coverage

`MERGED (merged 2026-05-31)` · nathanpond · opened 2026-05-31 · `codex/playwright-tests` → `master`

## Summary

- inventory the SPA routes and existing browser coverage in a 68-item Playwright backlog
- add systematic .NET Playwright E2E coverage for documents, workflow studio, pages and menus, IAM, notes, forms, assistant conversations, admin operations, notifications, queries, dashboards, and workflow tasks
- document deterministic fixture or product prerequisites for the remaining blocked browser flows
- fix the small production issues exposed by the new coverage, including DOCX multipart uploads and cache refresh behavior

## Validation

- `npm ci`
- `npx playwright install --with-deps`
- `npm run lint --if-present`
- `npm test --if-present`
- `npx playwright test` reports `Error: No tests found` because this repository uses the .NET Playwright runner
- `dotnet test tests/AutoNate.E2E.Tests --no-build`: 108 passed, 2 skipped, 0 failed before merging latest `master`
- `git diff --check`

## Notes

- The two skipped reproducers cover DOCX import commit behavior and appearance site-name persistence.
- Latest `master` was merged into this branch as `882a8a9a` without conflicts.

---

## archived-3 — Bump mermaid from 11.15.0 to 11.17.2 in /src/AutoNate.Spa

`MERGED (merged 2026-08-31)` · app/dependabot · opened 2026-08-31 · `dependabot/npm_and_yarn/src/AutoNate.Spa/mermaid-11.17.2` → `master`

Bumps [mermaid](https://github.com/mermaid-js/mermaid) from 11.15.0 to 11.17.2.
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/mermaid-js/mermaid/releases">mermaid's releases</a>.</em></p>
<blockquote>
<h2>mermaid@11.17.2</h2>
<h3>Patch Changes</h3>
<ul>
<li><a href="https://redirect.github.com/mermaid-js/mermaid/pull/8125">#8125</a> <a href="https://github.com/mermaid-js/mermaid/commit/178d7c79fcbafcf0662b822ec34ed989372ee5c2"><code>178d7c7</code></a> Thanks <a href="https://github.com/knsv-bot"><code>@​knsv-bot</code></a>! - fix: restore the <code>edgePaths</code> class on the edge group in rendered SVG, and point the flowchart, block and user journey stylesheets at it</li>
</ul>
<h2>mermaid@11.17.1</h2>
<h3>Patch Changes</h3>
<ul>
<li>
<p><a href="https://redirect.github.com/mermaid-js/mermaid/pull/8092">#8092</a> <a href="https://github.com/mermaid-js/mermaid/commit/31ce60a596746c76dc932ab540d910a6c7fff8be"><code>31ce60a</code></a> Thanks <a href="https://github.com/pbrolin47"><code>@​pbrolin47</code></a>! - fix(c4): wrap element labels to <code>c4.width</code> again</p>
<p>C4 element labels (<code>System</code>, <code>Container</code>, <code>Component</code>, <code>Person</code> and their <code>_Ext</code> variants) stopped wrapping in 11.17.0, so long descriptions rendered on one unbroken line and the shape grew sideways well past the configured <code>c4.width</code>. The unified-shapes label helper gated wrapping on the root-level <code>wrap</code> option, which has no schema default and is therefore <code>undefined</code>; it now gates on <code>c4.wrap</code> (default <code>true</code>), which is what the legacy renderer used.</p>
</li>
<li>
<p><a href="https://redirect.github.com/mermaid-js/mermaid/pull/8088">#8088</a> <a href="https://github.com/mermaid-js/mermaid/commit/c66200bc2302006c908f77819c584109f50c06e7"><code>c66200b</code></a> Thanks <a href="https://github.com/ashishjain0512"><code>@​ashishjain0512</code></a>! - fix: neo-look arrowheads and crow's-foot markers no longer fall back to default theme colours/stroke widths on the first render with <code>layout: elk</code>. State diagram arrowheads stayed dark on dark themes, and ER / requirement markers were drawn at the default stroke width, because markers were created from the layout package's own bundled copy of mermaid, whose config had not been initialized yet.</p>
</li>
<li>
<p><a href="https://redirect.github.com/mermaid-js/mermaid/pull/8079">#8079</a> <a href="https://github.com/mermaid-js/mermaid/commit/281cd7b0705a7cdf4295bfd5e3171647dc809dfb"><code>281cd7b</code></a> Thanks <a href="https://github.com/ashishjain0512"><code>@​ashishjain0512</code></a>! - fix(class): class diagram relation markers (composition, aggregation, extension, dependency, lollipop) no longer scale with the edge stroke width, so they stay outside the class box boundary in themes that set <code>strokeWidth: 2</code> (<code>redux</code>, <code>redux-dark</code>, <code>redux-color</code>, <code>redux-dark-color</code>, <code>neo</code>, <code>neo-dark</code>) with the default <code>classic</code> look.</p>
</li>
</ul>
<h2>mermaid@11.17.0</h2>
<h3>Minor Changes</h3>
<ul>
<li>
<p><a href="https://redirect.github.com/mermaid-js/mermaid/pull/7842">#7842</a> <a href="https://github.com/mermaid-js/mermaid/commit/3670b4e2d99b27945240dd3fe71da9175fddcaec"><code>3670b4e</code></a> Thanks <a href="https://github.com/filipsajdak"><code>@​filipsajdak</code></a>! - feat(c4): render C4 elements through the unified shape system, using the new person shape</p>
</li>
<li>
<p><a href="https://redirect.github.com/mermaid-js/mermaid/pull/7812">#7812</a> <a href="https://github.com/mermaid-js/mermaid/commit/cdfc0ea65f47bc8f9605a2a646ed87c25a692216"><code>cdfc0ea</code></a> Thanks <a href="https://github.com/knsv-bot"><code>@​knsv-bot</code></a>! - feat(class): route <code>classDiagram</code> to the unified (v2) renderer by default</p>
<p>Set <code>class: { defaultRenderer: 'dagre-d3' }</code> in the config to restore the legacy renderer.</p>
</li>
<li>
<p><a href="https://redirect.github.com/mermaid-js/mermaid/pull/7785">#7785</a> <a href="https://github.com/mermaid-js/mermaid/commit/c45cde9582ede4add658f62b771ba2a7efadde83"><code>c45cde9</code></a> Thanks <a href="https://github.com/knsv-bot"><code>@​knsv-bot</code></a>! - feat(flowchart): add collapsible flowchart subgraphs via <code>subgraphId@{ view: collapsed }</code></p>
</li>
<li>
<p><a href="https://redirect.github.com/mermaid-js/mermaid/pull/7828">#7828</a> <a href="https://github.com/mermaid-js/mermaid/commit/8eb3afc08c64e0f5d2b2447daac417250a202c13"><code>8eb3afc</code></a> Thanks <a href="https://github.com/knsv-bot"><code>@​knsv-bot</code></a>! - feat(elk): add <code>elk.keepEntryNodeOnTop</code> config option to keep a recursive flow's entry node on top</p>
</li>
<li>
<p><a href="https://redirect.github.com/mermaid-js/mermaid/pull/7803">#7803</a> <a href="https://github.com/mermaid-js/mermaid/commit/74e44ebf86d293cee1f2314c8b8a163284ea3911"><code>74e44eb</code></a> Thanks <a href="https://github.com/knsv-bot"><code>@​knsv-bot</code></a>! - feat(elk): add <code>elk.nodePlacementAlignment</code> config option</p>
</li>
<li>
<p><a href="https://redirect.github.com/mermaid-js/mermaid/pull/7792">#7792</a> <a href="https://github.com/mermaid-js/mermaid/commit/ea55b31bcfb36cfdfbc31a531058ee8c4ee53a4f"><code>ea55b31</code></a> Thanks <a href="https://github.com/RodrigojndSantos"><code>@​RodrigojndSantos</code></a>! - feat(er): add subgraph support to ER diagrams.</p>
</li>
<li>
<p><a href="https://redirect.github.com/mermaid-js/mermaid/pull/7970">#7970</a> <a href="https://github.com/mermaid-js/mermaid/commit/a2c0fb6cdf8073b8feb10595ea3cccff0237049b"><code>a2c0fb6</code></a> Thanks <a href="https://github.com/filipsajdak"><code>@​filipsajdak</code></a>! - feat(flowchart): add <code>folder</code>, <code>bucket</code>, <code>console</code> (terminal window) and <code>browser</code> shapes</p>
</li>
<li>
<p><a href="https://redirect.github.com/mermaid-js/mermaid/pull/7842">#7842</a> <a href="https://github.com/mermaid-js/mermaid/commit/ae3e1157c166fab7520d9ee2ed67b16613f6c243"><code>ae3e115</code></a> Thanks <a href="https://github.com/filipsajdak"><code>@​filipsajdak</code></a>! - feat(flowchart): add <code>person</code> shape (circular head above a rounded body), usable in flowcharts via <code>A@{ shape: person }</code></p>
</li>
<li>
<p><a href="https://redirect.github.com/mermaid-js/mermaid/pull/7724">#7724</a> <a href="https://github.com/mermaid-js/mermaid/commit/0fd7a9fe0d10a1ac39359bc5cb5341b5010a624e"><code>0fd7a9f</code></a> Thanks <a href="https://github.com/xdumaine"><code>@​xdumaine</code></a>! - feat(xyChart): add legends for named line and bar series</p>
</li>
</ul>
<h3>Patch Changes</h3>
<ul>
<li>
<p><a href="https://redirect.github.com/mermaid-js/mermaid/pull/7847">#7847</a> <a href="https://github.com/mermaid-js/mermaid/commit/215fe89d3ecfb47cf0836cb52bf272b14fc99f29"><code>215fe89</code></a> Thanks <a href="https://github.com/filipsajdak"><code>@​filipsajdak</code></a>! - fix(c4): named attributes such as <code>$tags</code>, <code>$link</code> and <code>$sprite</code> are no longer clobbered to undefined when they arrive in an earlier positional slot of Person/System/Container/Component/Boundary/Rel statements.</p>
</li>
<li>
<p><a href="https://redirect.github.com/mermaid-js/mermaid/pull/7871">#7871</a> <a href="https://github.com/mermaid-js/mermaid/commit/8d874c49fa1699cf22e99d4936b16f16dde1fc7f"><code>8d874c4</code></a> Thanks <a href="https://github.com/knsv-bot"><code>@​knsv-bot</code></a>! - fix(flowchart): stop dagre layout from spamming <code>warn</code>-level logs on every node/edge/cluster</p>
</li>
<li>
<p><a href="https://redirect.github.com/mermaid-js/mermaid/pull/8071">#8071</a> <a href="https://github.com/mermaid-js/mermaid/commit/b3d1f6316717faf099cbe21c9fb9f41c2e0bc069"><code>b3d1f63</code></a> Thanks <a href="https://github.com/pbrolin47"><code>@​pbrolin47</code></a>! - fix(block): sibling blocks overlapping in block diagrams when one has a label wider than 200px</p>
</li>
<li>
<p><a href="https://redirect.github.com/mermaid-js/mermaid/pull/7870">#7870</a> <a href="https://github.com/mermaid-js/mermaid/commit/71b8843fb5ae25d7b884f5cc7ba856d978e0420b"><code>71b8843</code></a> Thanks <a href="https://github.com/knsv-bot"><code>@​knsv-bot</code></a>! - fix: a <code>RangeError: Invalid array length</code> crash when rendering certain edges.</p>
</li>
<li>
<p><a href="https://redirect.github.com/mermaid-js/mermaid/pull/7924">#7924</a> <a href="https://github.com/mermaid-js/mermaid/commit/9cbef5d94f3aa6bea04b44f23ad81c1b8d7ca2b7"><code>9cbef5d</code></a> Thanks <a href="https://github.com/nightt5879"><code>@​nightt5879</code></a>! - fix(treeView): icons disappearing after strict security sanitization.</p>
</li>
</ul>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/mermaid-js/mermaid/commit/dcb694ddb58dc5ad3502e7e903cac05fd812eac3"><code>dcb694d</code></a> Version Packages (<a href="https://redirect.github.com/mermaid-js/mermaid/issues/8130">#8130</a>)</li>
<li><a href="https://github.com/mermaid-js/mermaid/commit/178d7c79fcbafcf0662b822ec34ed989372ee5c2"><code>178d7c7</code></a> fix: restore edgePaths class on the edge group (<a href="https://redirect.github.com/mermaid-js/mermaid/issues/8125">#8125</a>)</li>
<li><a href="https://github.com/mermaid-js/mermaid/commit/569f46e2617f6627e00e56f9a3669369fc86f9c1"><code>569f46e</code></a> Version Packages (<a href="https://redirect.github.com/mermaid-js/mermaid/issues/8114">#8114</a>)</li>
<li><a href="https://github.com/mermaid-js/mermaid/commit/3054836688bfa5cef4abca757548e5da6da962b9"><code>3054836</code></a> Version Packages</li>
<li><a href="https://github.com/mermaid-js/mermaid/commit/5b17e0a38f8e1094f1fd62b7f74a2877c3cbf0b8"><code>5b17e0a</code></a> Merge pull request <a href="https://redirect.github.com/mermaid-js/mermaid/issues/8092">#8092</a> from mermaid-js/hotfix/11.17.1</li>
<li><a href="https://github.com/mermaid-js/mermaid/commit/655211a0657add5136f8358756dd5294c6dcd821"><code>655211a</code></a> Reverted change of wrap-options</li>
<li><a href="https://github.com/mermaid-js/mermaid/commit/8a2348020038cd908a819685f555b16694f3d490"><code>8a23480</code></a> Updated doc from feedback</li>
<li><a href="https://github.com/mermaid-js/mermaid/commit/412c80abb749da9844322855378140fc2b2a6877"><code>412c80a</code></a> Update doc and consistent handlig in c4 as for seq diags</li>
<li><a href="https://github.com/mermaid-js/mermaid/commit/b433a9aad549f650d268309b4ac1af180502cea3"><code>b433a9a</code></a> Updated changeset to describe specifik diagram affected</li>
<li><a href="https://github.com/mermaid-js/mermaid/commit/6bae15eb59d2836faf45e6642b24f4a039526d62"><code>6bae15e</code></a> Updated tests to Playwright API</li>
<li>Additional commits viewable in <a href="https://github.com/mermaid-js/mermaid/compare/mermaid@11.15.0...mermaid@11.17.2">compare view</a></li>
</ul>
</details>
<br />


[![Dependabot compatibility score](https://dependabot-badges.githubapp.com/badges/compatibility_score?dependency-name=mermaid&package-manager=npm_and_yarn&previous-version=11.15.0&new-version=11.17.2)](https://docs.github.com/en/github/managing-security-vulnerabilities/about-dependabot-security-updates#about-compatibility-scores)

Dependabot will resolve any conflicts with this PR as long as you don't alter it yourself. You can also trigger a rebase manually by commenting `@dependabot rebase`.

[//]: # (dependabot-automerge-start)
[//]: # (dependabot-automerge-end)

---

<details>
<summary>Dependabot commands and options</summary>
<br />

You can trigger Dependabot actions by commenting on this PR:
- `@dependabot rebase` will rebase this PR
- `@dependabot recreate` will recreate this PR, overwriting any edits that have been made to it
- `@dependabot show <dependency name> ignore conditions` will show all of the ignore conditions of the specified dependency
- `@dependabot ignore this major version` will close this PR and stop Dependabot creating any more for this major version (unless you reopen the PR or upgrade to it yourself)
- `@dependabot ignore this minor version` will close this PR and stop Dependabot creating any more for this minor version (unless you reopen the PR or upgrade to it yourself)
- `@dependabot ignore this dependency` will close this PR and stop Dependabot creating any more for this dependency (unless you reopen the PR or upgrade to it yourself)
You can disable automated security fix PRs for this repo from the [Security Alerts page](https://github.com/nathanpond/AutoNate/network/alerts).

</details>

---

## archived-4 — Bump js-yaml from 4.1.1 to 4.3.2 in /src/AutoNate.Spa

`MERGED (merged 2026-08-31)` · app/dependabot · opened 2026-08-31 · `dependabot/npm_and_yarn/src/AutoNate.Spa/js-yaml-4.3.2` → `master`

Bumps [js-yaml](https://github.com/nodeca/js-yaml) from 4.1.1 to 4.3.2.
<details>
<summary>Changelog</summary>
<p><em>Sourced from <a href="https://github.com/nodeca/js-yaml/blob/4.3.2/CHANGELOG.md">js-yaml's changelog</a>.</em></p>
<blockquote>
<h2>4.3.2 - 2026-08-26</h2>
<h3>Changed</h3>
<ul>
<li>[backport] Hard-limit merge sequence size to 100.</li>
</ul>
<h3>Security</h3>
<ul>
<li>[backport] Count empty mappings in merge sequences toward <code>maxTotalMergeKeys</code>
to limit CPU usage, <a href="https://redirect.github.com/nodeca/js-yaml/issues/797">#797</a>.</li>
</ul>
<h2>4.3.1 - 2026-07-31</h2>
<h3>Security</h3>
<ul>
<li>[backport] Remove quadratic complexity from <code>!!omap</code> duplicate key detection.</li>
</ul>
<h2>4.3.0 - 2026-06-27</h2>
<h3>Added</h3>
<ul>
<li>[backport] Added <code>maxTotalMergeKeys</code> (10000) loader option to limit the total number of
keys processed by YAML merge (<code>&lt;&lt;</code>) across one <code>load()</code> / <code>loadAll()</code> call.</li>
</ul>
<h3>Fixed</h3>
<ul>
<li>Restore umd builds back to es5.</li>
</ul>
<h3>Removed</h3>
<ul>
<li>[backport] <code>maxMergeSeqLength</code> replaced with <code>maxTotalMergeKeys</code> for limiting YAML merge
processing.</li>
</ul>
<h2>[4.2.0] - 2026-06-01</h2>
<h3>Added</h3>
<ul>
<li>Added <code>docs/safety.md</code> with notes about processing untrusted YAML.</li>
<li>Added <code>maxDepth</code> (100) loader option. Not a problem, but gives a better
exception instead of RangeError on stack overflow.</li>
<li>Added <code>maxMergeSeqLength</code> (20) loader option. Not a problem after <code>merge</code> fix,
but an additional restriction for safety.</li>
<li>Added sourcemaps to <code>dist/</code> builds.</li>
</ul>
<h3>Changed</h3>
<ul>
<li>Stop resolving numbers with underscores as numeric scalars, <a href="https://redirect.github.com/nodeca/js-yaml/issues/627">#627</a>.</li>
<li>Switched dev toolchains to Vite / neostandard.</li>
<li>Updated demo.</li>
<li>Reorganized tests.</li>
<li><code>dist/</code> files are no longer kept in the repository.</li>
</ul>
<h3>Fixed</h3>
<ul>
<li>Fix parsing of properties on the first implicit block mapping key, <a href="https://redirect.github.com/nodeca/js-yaml/issues/62">#62</a>.</li>
<li>Fix trailing whitespace handling when folding flow scalar lines, <a href="https://redirect.github.com/nodeca/js-yaml/issues/307">#307</a>.</li>
<li>Reject top-level block scalars without content indentation, <a href="https://redirect.github.com/nodeca/js-yaml/issues/280">#280</a>.</li>
<li>Ensure numbers survive round-trip, <a href="https://redirect.github.com/nodeca/js-yaml/issues/737">#737</a>.</li>
<li>Fix test coverage for issue <a href="https://redirect.github.com/nodeca/js-yaml/issues/221">#221</a>.</li>
<li>Fix flow scalar trailing whitespace folding, <a href="https://redirect.github.com/nodeca/js-yaml/issues/307">#307</a>.</li>
</ul>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/nodeca/js-yaml/commit/79ca68d90f333fbe6d9e42827527e62636200191"><code>79ca68d</code></a> 4.3.2 released</li>
<li><a href="https://github.com/nodeca/js-yaml/commit/d90b6612a5a84385bdcb556c44578eac76dc0f6b"><code>d90b661</code></a> Backport merge limits from v5.4.1</li>
<li><a href="https://github.com/nodeca/js-yaml/commit/86e91b815b8794c3c73a179c1770871e37ec2df8"><code>86e91b8</code></a> 4.3.1 released</li>
<li><a href="https://github.com/nodeca/js-yaml/commit/c3cc4b0bb9ddb9af2dd9b61e0d56f5ce7983cd4a"><code>c3cc4b0</code></a> Backport quadratic complexity fix for !!omap</li>
<li><a href="https://github.com/nodeca/js-yaml/commit/33d05b5d29a8c21360f620f7e1c1706e24522eda"><code>33d05b5</code></a> 4.3.0 released</li>
<li><a href="https://github.com/nodeca/js-yaml/commit/663bfab6db2b4a146a9366fd685f069345be4ddb"><code>663bfab</code></a> Drop demo publish, to not override new v5 one.</li>
<li><a href="https://github.com/nodeca/js-yaml/commit/1cb8c7b94bf75e15116869c1c0482dcb22785986"><code>1cb8c7b</code></a> Add v4-legacy tag for publish</li>
<li><a href="https://github.com/nodeca/js-yaml/commit/02f27afad532763263cd2b6be35c24ee8e1f6157"><code>02f27af</code></a> Restore umd builds back to es5</li>
<li><a href="https://github.com/nodeca/js-yaml/commit/8be84edaf15e7c394fa3b813179d1bcc280e87fb"><code>8be84ed</code></a> Fix es5 compatibility</li>
<li><a href="https://github.com/nodeca/js-yaml/commit/59423c6f8cdc78742ac00e25a4dd39ef16b702e4"><code>59423c6</code></a> Replace <code>maxMergeSeqLength</code> option with <code>maxTotalMergeKeys</code> (more robust). Ba...</li>
<li>Additional commits viewable in <a href="https://github.com/nodeca/js-yaml/compare/4.1.1...4.3.2">compare view</a></li>
</ul>
</details>
<br />


[![Dependabot compatibility score](https://dependabot-badges.githubapp.com/badges/compatibility_score?dependency-name=js-yaml&package-manager=npm_and_yarn&previous-version=4.1.1&new-version=4.3.2)](https://docs.github.com/en/github/managing-security-vulnerabilities/about-dependabot-security-updates#about-compatibility-scores)

Dependabot will resolve any conflicts with this PR as long as you don't alter it yourself. You can also trigger a rebase manually by commenting `@dependabot rebase`.

[//]: # (dependabot-automerge-start)
[//]: # (dependabot-automerge-end)

---

<details>
<summary>Dependabot commands and options</summary>
<br />

You can trigger Dependabot actions by commenting on this PR:
- `@dependabot rebase` will rebase this PR
- `@dependabot recreate` will recreate this PR, overwriting any edits that have been made to it
- `@dependabot show <dependency name> ignore conditions` will show all of the ignore conditions of the specified dependency
- `@dependabot ignore this major version` will close this PR and stop Dependabot creating any more for this major version (unless you reopen the PR or upgrade to it yourself)
- `@dependabot ignore this minor version` will close this PR and stop Dependabot creating any more for this minor version (unless you reopen the PR or upgrade to it yourself)
- `@dependabot ignore this dependency` will close this PR and stop Dependabot creating any more for this dependency (unless you reopen the PR or upgrade to it yourself)
You can disable automated security fix PRs for this repo from the [Security Alerts page](https://github.com/nathanpond/AutoNate/network/alerts).

</details>

---

## archived-5 — Bump dompurify from 3.4.3 to 3.4.14 in /src/AutoNate.Spa

`MERGED (merged 2026-08-31)` · app/dependabot · opened 2026-08-31 · `dependabot/npm_and_yarn/src/AutoNate.Spa/dompurify-3.4.14` → `master`

Bumps [dompurify](https://github.com/cure53/DOMPurify) from 3.4.3 to 3.4.14.
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/cure53/DOMPurify/releases">dompurify's releases</a>.</em></p>
<blockquote>
<h2>DOMPurify 3.4.14</h2>
<ul>
<li>Fixed an issue with possible bypasses when risky tags are allow-listed, thanks <a href="https://github.com/AlirezaRouhbakhsh"><code>@​AlirezaRouhbakhsh</code></a></li>
<li>Fixed a couple of edge cases with mixed document contexts, thanks <a href="https://github.com/fishjojo1"><code>@​fishjojo1</code></a></li>
<li>Added the SVG <code>pointer-events</code> and <code>vector-effect</code> presentation attributes to the allow-list, thanks <a href="https://github.com/Jaybhade"><code>@​Jaybhade</code></a></li>
<li>Conducted another refactoring run, removed dead branches and duplicated logic, flattened attribute validation</li>
<li>Updated the documentation in several spots, README, wiki, etc., thanks <a href="https://github.com/Akokonunes"><code>@​Akokonunes</code></a></li>
<li>Updated several development dependencies and CI workflow actions</li>
</ul>
<h2>DOMPurify 3.4.13</h2>
<ul>
<li>Fixed an issue with hook removal during <code>IN_PLACE</code> sanitization, thanks <a href="https://github.com/koyokr"><code>@​koyokr</code></a></li>
<li>Fixed an issue with hooks potentially bypassing the clone guard, thanks <a href="https://github.com/AkshayjainG"><code>@​AkshayjainG</code></a></li>
<li>Fixed an issue with DOM clobbering via <code>ownerDocument</code> during <code>IN_PLACE</code>, thanks <a href="https://github.com/AkshayjainG"><code>@​AkshayjainG</code></a></li>
<li>Bumped several dependencies where possible</li>
</ul>
<h2>DOMPurify 3.4.12</h2>
<ul>
<li>Fixed an issue where a hook would not get called for custom elements, thanks <a href="https://github.com/Rikuxx0"><code>@​Rikuxx0</code></a></li>
<li>Hardened the handling of hooks removing elements, <a href="https://github.com/mkrause-bee360"><code>@​mkrause-bee360</code></a></li>
<li>Added support for a few new SVG attributes, thanks <a href="https://github.com/cbn-falias"><code>@​cbn-falias</code></a> &amp; <a href="https://github.com/Develop-KIM"><code>@​Develop-KIM</code></a></li>
<li>Hardened the handling of declarative partial updates</li>
<li>Updated the documentation is several spots, README, wiki, etc.</li>
<li>Bumped several dependencies where possible</li>
</ul>
<h2>DOMPurify 3.4.11</h2>
<ul>
<li>Fixed an issue with a leaky config for hooks via <code>setConfig</code>, thanks <a href="https://github.com/trace37labs"><code>@​trace37labs</code></a></li>
<li>Bumped vulnerable development dependencies to arrive at plain 0 with <code>npm audit</code></li>
<li>Updated the <code>osv-scanner</code> suppression list as no vulnerable dependencies are left for now</li>
<li>Updated up the linting tool-chain and removed now-redundant lint directives</li>
<li>Updated the documentation is several spots, README, wiki, etc.</li>
<li>Bumped several dependencies where possible</li>
</ul>
<h2>DOMPurify 3.4.10</h2>
<ul>
<li>Refactored codebase for clarity: extracted the public type declarations into <code>types.ts</code></li>
<li>Decomposed the three largest sanitizer functions into focused helpers</li>
<li>Removed duplicated defaults and dead branches, consolidated <code>SAFE_FOR_TEMPLATES</code> scrubbing into single shared path</li>
<li>Improved per-node performance by hoisting the mXSS probe regexes and testing <code>textContent</code> before <code>innerHTML</code></li>
<li>Added a deterministic micro-benchmark harness (<code>npm run bench</code>) with a <code>--compare</code> mode</li>
<li>Reduced CI cost by running the full three-engine browser suite once per PR</li>
<li>Refreshed the <code>demos/</code> folder so every demo runs again, and added a SVG-via-<code>&lt;img&gt;</code> demo</li>
<li>Documented the bench and <code>test:happydom</code> scripts in the README</li>
<li>Completed the Attack Classes &amp; Bypass History wiki page</li>
<li>Bumped several dependencies where possible</li>
</ul>
<h2>DOMPurify 3.4.9</h2>
<ul>
<li>Further improved the handling of Trusted Types config options, thanks <a href="https://github.com/offset"><code>@​offset</code></a></li>
<li>Further improved the handling of <code>IN_PLACE</code> sanitization, thanks <a href="https://github.com/mozfreddyb"><code>@​mozfreddyb</code></a></li>
<li>Added more test coverage for <code>IN_PLACE</code> and Trusted Types related usage</li>
<li>Bumped several dependencies where possible</li>
<li>Updated README and wiki with more accurate documentation &amp; attack samples</li>
</ul>
<h2>DOMPurify 3.4.8</h2>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/cure53/DOMPurify/commit/4e6fe24173f1a85eafacd95e3c82966e29d34d49"><code>4e6fe24</code></a> release: 3.4.14 (<a href="https://redirect.github.com/cure53/DOMPurify/issues/1587">#1587</a>)</li>
<li><a href="https://github.com/cure53/DOMPurify/commit/3067f774676975de12306effd6db6ad7a9a8c17f"><code>3067f77</code></a> release: 3.4.13 (<a href="https://redirect.github.com/cure53/DOMPurify/issues/1562">#1562</a>)</li>
<li><a href="https://github.com/cure53/DOMPurify/commit/a9ca1e537422319a557a9a2aa61f003b23b4a197"><code>a9ca1e5</code></a> release: 3.4.12 (<a href="https://redirect.github.com/cure53/DOMPurify/issues/1537">#1537</a>)</li>
<li><a href="https://github.com/cure53/DOMPurify/commit/0cae5187403132f96a6d357649e4b15633fc210a"><code>0cae518</code></a> release: 3.4.11 (<a href="https://redirect.github.com/cure53/DOMPurify/issues/1494">#1494</a>)</li>
<li><a href="https://github.com/cure53/DOMPurify/commit/6ee5716f8336989753611beeca364957c0eb0c3e"><code>6ee5716</code></a> release: 3.4.10 (<a href="https://redirect.github.com/cure53/DOMPurify/issues/1478">#1478</a>)</li>
<li><a href="https://github.com/cure53/DOMPurify/commit/52102472d46035857c52df19e44285f8a1e102fc"><code>5210247</code></a> release: 3.4.9 (<a href="https://redirect.github.com/cure53/DOMPurify/issues/1459">#1459</a>)</li>
<li><a href="https://github.com/cure53/DOMPurify/commit/bcdd8285412dc9c4c149652aed2d712e790d6ccf"><code>bcdd828</code></a> release: 3.4.8 (<a href="https://redirect.github.com/cure53/DOMPurify/issues/1439">#1439</a>)</li>
<li><a href="https://github.com/cure53/DOMPurify/commit/ca30f070c360df162a3e3848e80e6fd3c9e74bff"><code>ca30f07</code></a> release: 3.4.7 (<a href="https://redirect.github.com/cure53/DOMPurify/issues/1414">#1414</a>)</li>
<li><a href="https://github.com/cure53/DOMPurify/commit/bb7739e5bccec7e1ab3dae3f3e42d02db3acaaae"><code>bb7739e</code></a> release: 3.4.6 (<a href="https://redirect.github.com/cure53/DOMPurify/issues/1394">#1394</a>)</li>
<li><a href="https://github.com/cure53/DOMPurify/commit/011b0c78f2a0f57ee54f5fcccb697a46ca6e63ea"><code>011b0c7</code></a> release: 3.4.5 (<a href="https://redirect.github.com/cure53/DOMPurify/issues/1382">#1382</a>)</li>
<li>Additional commits viewable in <a href="https://github.com/cure53/DOMPurify/compare/3.4.3...3.4.14">compare view</a></li>
</ul>
</details>
<br />


[![Dependabot compatibility score](https://dependabot-badges.githubapp.com/badges/compatibility_score?dependency-name=dompurify&package-manager=npm_and_yarn&previous-version=3.4.3&new-version=3.4.14)](https://docs.github.com/en/github/managing-security-vulnerabilities/about-dependabot-security-updates#about-compatibility-scores)

Dependabot will resolve any conflicts with this PR as long as you don't alter it yourself. You can also trigger a rebase manually by commenting `@dependabot rebase`.

[//]: # (dependabot-automerge-start)
[//]: # (dependabot-automerge-end)

---

<details>
<summary>Dependabot commands and options</summary>
<br />

You can trigger Dependabot actions by commenting on this PR:
- `@dependabot rebase` will rebase this PR
- `@dependabot recreate` will recreate this PR, overwriting any edits that have been made to it
- `@dependabot show <dependency name> ignore conditions` will show all of the ignore conditions of the specified dependency
- `@dependabot ignore this major version` will close this PR and stop Dependabot creating any more for this major version (unless you reopen the PR or upgrade to it yourself)
- `@dependabot ignore this minor version` will close this PR and stop Dependabot creating any more for this minor version (unless you reopen the PR or upgrade to it yourself)
- `@dependabot ignore this dependency` will close this PR and stop Dependabot creating any more for this dependency (unless you reopen the PR or upgrade to it yourself)
You can disable automated security fix PRs for this repo from the [Security Alerts page](https://github.com/nathanpond/AutoNate/network/alerts).

</details>

---

## archived-6 — Bump react-router and react-router-dom in /src/AutoNate.Spa

`MERGED (merged 2026-08-31)` · app/dependabot · opened 2026-08-31 · `dependabot/npm_and_yarn/src/AutoNate.Spa/multi-9ceb6b67f2` → `master`

Bumps [react-router](https://github.com/remix-run/react-router/tree/HEAD/packages/react-router) to 7.18.3 and updates ancestor dependency [react-router-dom](https://github.com/remix-run/react-router/tree/HEAD/packages/react-router-dom). These dependencies need to be updated together.

Updates `react-router` from 7.14.2 to 7.18.3
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/remix-run/react-router/releases">react-router's releases</a>.</em></p>
<blockquote>
<h2>v7.18.3</h2>
<p>See the changelog for release notes: <a href="https://github.com/remix-run/react-router/blob/v7/CHANGELOG.md#v7183">https://github.com/remix-run/react-router/blob/v7/CHANGELOG.md#v7183</a></p>
<h2>v7.18.2</h2>
<p>See the changelog for release notes: <a href="https://github.com/remix-run/react-router/blob/v7/CHANGELOG.md#v7182">https://github.com/remix-run/react-router/blob/v7/CHANGELOG.md#v7182</a></p>
<h2>v7.18.1</h2>
<p>See the changelog for release notes: <a href="https://github.com/remix-run/react-router/blob/v7/CHANGELOG.md#v7181">https://github.com/remix-run/react-router/blob/v7/CHANGELOG.md#v7181</a></p>
<h2>v7.18.0</h2>
<p>See the changelog for release notes: <a href="https://github.com/remix-run/react-router/blob/main/CHANGELOG.md#v7180">https://github.com/remix-run/react-router/blob/main/CHANGELOG.md#v7180</a></p>
<h2>v7.17.0</h2>
<p>See the changelog for release notes: <a href="https://github.com/remix-run/react-router/blob/main/CHANGELOG.md#v7170">https://github.com/remix-run/react-router/blob/main/CHANGELOG.md#v7170</a></p>
<h2>v7.16.0</h2>
<p>See the changelog for release notes: <a href="https://github.com/remix-run/react-router/blob/main/CHANGELOG.md#v7160">https://github.com/remix-run/react-router/blob/main/CHANGELOG.md#v7160</a></p>
<h2>v7.15.1</h2>
<p>See the changelog for release notes: <a href="https://github.com/remix-run/react-router/blob/main/CHANGELOG.md#v7151">https://github.com/remix-run/react-router/blob/main/CHANGELOG.md#v7151</a></p>
<h2>v7.15.0</h2>
<p>See the changelog for release notes: <a href="https://github.com/remix-run/react-router/blob/main/CHANGELOG.md#v7150">https://github.com/remix-run/react-router/blob/main/CHANGELOG.md#v7150</a></p>
</blockquote>
</details>
<details>
<summary>Changelog</summary>
<p><em>Sourced from <a href="https://github.com/remix-run/react-router/blob/react-router@7.18.3/packages/react-router/CHANGELOG.md">react-router's changelog</a>.</em></p>
<blockquote>
<h2>v7.18.3</h2>
<h3>Patch Changes</h3>
<ul>
<li>Improve route matching performance for long paths (<a href="https://redirect.github.com/remix-run/react-router/pull/15423">#15423</a>)</li>
<li>Improve validation of action request origins (<a href="https://redirect.github.com/remix-run/react-router/pull/15420">#15420</a>)</li>
<li>Add additional URL validation on client side navigations/redirects (<a href="https://redirect.github.com/remix-run/react-router/pull/15446">#15446</a>)</li>
</ul>
<h2>v7.18.2</h2>
<h3>Patch Changes</h3>
<ul>
<li>Harden RSC CSRF codepaths. (<a href="https://redirect.github.com/remix-run/react-router/pull/15353">#15353</a>)</li>
</ul>
<h2>v7.18.1</h2>
<h3>Patch Changes</h3>
<ul>
<li><em>No changes</em></li>
</ul>
<h2>v7.18.0</h2>
<h3>Patch Changes</h3>
<ul>
<li>Fix server handler prerender responses when using <code>ssr: false</code> and <code>future.v8_trailingSlashAwareDataRequests: true</code>. Avoids false positive &quot;SPA Mode&quot; detection when serving prerendered paths (<a href="https://redirect.github.com/remix-run/react-router/pull/15173">#15173</a>)</li>
<li>Use the <code>ServerRouter</code> nonce for nonce-aware SSR components when they don't provide their own value so strict CSP pages can load them. (<a href="https://redirect.github.com/remix-run/react-router/pull/15170">#15170</a>)</li>
<li>Use <code>turbo-stream</code> to serialize and deserialize Framework Mode hydration errors (<a href="https://redirect.github.com/remix-run/react-router/pull/15175">#15175</a>)</li>
<li>Precompute route branch matchers to avoid recompiling route path regexes during matching (<a href="https://redirect.github.com/remix-run/react-router/pull/15186">#15186</a>)</li>
<li>Use the constructed request URL host when validating action request origins. (<a href="https://redirect.github.com/remix-run/react-router/pull/15185">#15185</a>)</li>
<li>Remove the un-documented custom error serialization logic from Data Mode SSR built-in hydration flows (<a href="https://redirect.github.com/remix-run/react-router/pull/15175">#15175</a>)</li>
<li>Validate protocols in RSC render redirects (<a href="https://redirect.github.com/remix-run/react-router/pull/15177">#15177</a>)</li>
<li>Consolidate url normalization logic and better handle mixed slashes (<a href="https://redirect.github.com/remix-run/react-router/pull/15176">#15176</a>)</li>
</ul>
<h2>v7.17.0</h2>
<h3>Minor Changes</h3>
<ul>
<li>Ship a subset of the official documentation inside the <code>react-router</code> package (<a href="https://redirect.github.com/remix-run/react-router/pull/15121">#15121</a>)
<ul>
<li>Markdown docs are now available in <code>node_modules/react-router/docs</code>, letting AI coding agents and the React Router agent skills read official docs locally</li>
<li>Excludes auto-generated API docs (<code>api/</code>), <code>community/</code> content, and tutorials (<code>tutorials/</code>)</li>
</ul>
</li>
</ul>
<h2>v7.16.0</h2>
<h3>Minor Changes</h3>
<ul>
<li>Stabilize <code>future.unstable_trailingSlashAwareDataRequests</code> as <code>future.v8_trailingSlashAwareDataRequests</code> (<a href="https://redirect.github.com/remix-run/react-router/pull/15098">#15098</a>)</li>
</ul>
<h3>Patch Changes</h3>
<ul>
<li>Disable manifest path when lazy route dicovery is disabled (<a href="https://redirect.github.com/remix-run/react-router/pull/15068">#15068</a>)</li>
</ul>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/remix-run/react-router/commit/23166dfe7f61323f0d2775af67d2691f9ed0843d"><code>23166df</code></a> Release v7.18.3 (<a href="https://github.com/remix-run/react-router/tree/HEAD/packages/react-router/issues/15424">#15424</a>)</li>
<li><a href="https://github.com/remix-run/react-router/commit/38af67f5cada15f0f540c597929a1b08022b46a5"><code>38af67f</code></a> Validate client-side navigation targets (<a href="https://github.com/remix-run/react-router/tree/HEAD/packages/react-router/issues/15446">#15446</a>)</li>
<li><a href="https://github.com/remix-run/react-router/commit/df78a297667c8891de29615f364a837ff858d45f"><code>df78a29</code></a> Revert &quot;fix: normalize control characters in relative URLs (<a href="https://github.com/remix-run/react-router/tree/HEAD/packages/react-router/issues/15422">#15422</a>)&quot; (<a href="https://github.com/remix-run/react-router/tree/HEAD/packages/react-router/issues/15441">#15441</a>)</li>
<li><a href="https://github.com/remix-run/react-router/commit/d1a4b1d4ad9e230fbe2653b60fed20dff6872f60"><code>d1a4b1d</code></a> fix: normalize control characters in relative URLs (<a href="https://github.com/remix-run/react-router/tree/HEAD/packages/react-router/issues/15422">#15422</a>)</li>
<li><a href="https://github.com/remix-run/react-router/commit/e7c2d94ca41e8c8d1312a3e004d6f88b252509db"><code>e7c2d94</code></a> Revert &quot;fix: normalize RSC server redirects (<a href="https://github.com/remix-run/react-router/tree/HEAD/packages/react-router/issues/15421">#15421</a>)&quot; (<a href="https://github.com/remix-run/react-router/tree/HEAD/packages/react-router/issues/15434">#15434</a>)</li>
<li><a href="https://github.com/remix-run/react-router/commit/e4b70d65d0188846baf0f1dad4bb05f6781186ea"><code>e4b70d6</code></a> fix: validate schemeful action origins (<a href="https://github.com/remix-run/react-router/tree/HEAD/packages/react-router/issues/15420">#15420</a>)</li>
<li><a href="https://github.com/remix-run/react-router/commit/b137cabaeb92b15b97233e75e0afa09f70307e84"><code>b137cab</code></a> fix: normalize RSC server redirects (<a href="https://github.com/remix-run/react-router/tree/HEAD/packages/react-router/issues/15421">#15421</a>)</li>
<li><a href="https://github.com/remix-run/react-router/commit/6cc6f7a657e5808a510627ac6d9c0456ddeda9f0"><code>6cc6f7a</code></a> fix: improve matching perf (<a href="https://github.com/remix-run/react-router/tree/HEAD/packages/react-router/issues/15423">#15423</a>)</li>
<li><a href="https://github.com/remix-run/react-router/commit/69a653ee6ab1ac95b13c917ec56c5f3dc17ca9c1"><code>69a653e</code></a> Release v7.18.2 (<a href="https://github.com/remix-run/react-router/tree/HEAD/packages/react-router/issues/15354">#15354</a>)</li>
<li><a href="https://github.com/remix-run/react-router/commit/8ebd5df9932854547963e3255c8454e62430e05d"><code>8ebd5df</code></a> Harden RSC CSRF codepaths (backport of <a href="https://github.com/remix-run/react-router/tree/HEAD/packages/react-router/issues/15311">#15311</a>) (<a href="https://github.com/remix-run/react-router/tree/HEAD/packages/react-router/issues/15353">#15353</a>)</li>
<li>Additional commits viewable in <a href="https://github.com/remix-run/react-router/commits/react-router@7.18.3/packages/react-router">compare view</a></li>
</ul>
</details>
<br />

Updates `react-router-dom` from 7.14.2 to 7.18.3
<details>
<summary>Changelog</summary>
<p><em>Sourced from <a href="https://github.com/remix-run/react-router/blob/react-router-dom@7.18.3/packages/react-router-dom/CHANGELOG.md">react-router-dom's changelog</a>.</em></p>
<blockquote>
<h2>v7.18.3</h2>
<h3>Patch Changes</h3>
<ul>
<li>Updated dependencies:
<ul>
<li><a href="https://github.com/remix-run/react-router/releases/tag/react-router@7.18.3"><code>react-router@7.18.3</code></a></li>
</ul>
</li>
</ul>
<h2>v7.18.2</h2>
<h3>Patch Changes</h3>
<ul>
<li>Updated dependencies:
<ul>
<li><a href="https://github.com/remix-run/react-router/releases/tag/react-router@7.18.2"><code>react-router@7.18.2</code></a></li>
</ul>
</li>
</ul>
<h2>v7.18.1</h2>
<h3>Patch Changes</h3>
<ul>
<li>Fix incorrect <code>package.json</code> <code>main</code> field for CommonJS builds (<a href="https://redirect.github.com/remix-run/react-router/pull/15238">#15238</a>)</li>
<li>Updated dependencies:
<ul>
<li><a href="https://github.com/remix-run/react-router/releases/tag/react-router@7.18.1"><code>react-router@7.18.1</code></a></li>
</ul>
</li>
</ul>
<h2>v7.18.0</h2>
<h3>Patch Changes</h3>
<ul>
<li>Updated dependencies:
<ul>
<li><a href="https://github.com/remix-run/react-router/releases/tag/react-router@7.18.0"><code>react-router@7.18.0</code></a></li>
</ul>
</li>
</ul>
<h2>v7.17.0</h2>
<h3>Patch Changes</h3>
<ul>
<li>Updated dependencies:
<ul>
<li><a href="https://github.com/remix-run/react-router/releases/tag/react-router@7.17.0"><code>react-router@7.17.0</code></a></li>
</ul>
</li>
</ul>
<h2>v7.16.0</h2>
<h3>Patch Changes</h3>
<ul>
<li>Remove stale/invalid <code>unpkg</code> field from <code>package.json</code>. This was removed from other packages with the release of v7 but missed in the <code>react-router-dom</code> re-export package (<a href="https://redirect.github.com/remix-run/react-router/pull/15075">#15075</a>)</li>
<li>Updated dependencies:
<ul>
<li><a href="https://github.com/remix-run/react-router/releases/tag/react-router@7.16.0"><code>react-router@7.16.0</code></a></li>
</ul>
</li>
</ul>
<h2>v7.15.1</h2>
<h3>Patch Changes</h3>
<ul>
<li>Updated dependencies:
<ul>
<li><a href="https://github.com/remix-run/react-router/releases/tag/react-router@7.15.1"><code>react-router@7.15.1</code></a></li>
</ul>
</li>
</ul>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/remix-run/react-router/commit/23166dfe7f61323f0d2775af67d2691f9ed0843d"><code>23166df</code></a> Release v7.18.3 (<a href="https://github.com/remix-run/react-router/tree/HEAD/packages/react-router-dom/issues/15424">#15424</a>)</li>
<li><a href="https://github.com/remix-run/react-router/commit/69a653ee6ab1ac95b13c917ec56c5f3dc17ca9c1"><code>69a653e</code></a> Release v7.18.2 (<a href="https://github.com/remix-run/react-router/tree/HEAD/packages/react-router-dom/issues/15354">#15354</a>)</li>
<li><a href="https://github.com/remix-run/react-router/commit/afdf85d3c15448a41017514caca2aca038d3e9ca"><code>afdf85d</code></a> Release v7.18.1 (<a href="https://github.com/remix-run/react-router/tree/HEAD/packages/react-router-dom/issues/15253">#15253</a>)</li>
<li><a href="https://github.com/remix-run/react-router/commit/2ecaa1ddbbcd583999dda46dd5413e907e8a46f3"><code>2ecaa1d</code></a> Fix react-router-dom main entry metadata (<a href="https://github.com/remix-run/react-router/tree/HEAD/packages/react-router-dom/issues/15238">#15238</a>)</li>
<li><a href="https://github.com/remix-run/react-router/commit/6fb1e79f8304eddd8b78759edea83cb32389ebf5"><code>6fb1e79</code></a> Release v7.18.0 (<a href="https://github.com/remix-run/react-router/tree/HEAD/packages/react-router-dom/issues/15187">#15187</a>)</li>
<li><a href="https://github.com/remix-run/react-router/commit/195a0d03c1417127ccee73853058c8521beb4fce"><code>195a0d0</code></a> Release v7.17.0 (<a href="https://github.com/remix-run/react-router/tree/HEAD/packages/react-router-dom/issues/15145">#15145</a>)</li>
<li><a href="https://github.com/remix-run/react-router/commit/8984d23f86ca7ae5655711744b77816090bda4e6"><code>8984d23</code></a> Release v7.16.0 (<a href="https://github.com/remix-run/react-router/tree/HEAD/packages/react-router-dom/issues/15105">#15105</a>)</li>
<li><a href="https://github.com/remix-run/react-router/commit/3ed77afcde0ad9aea79f1afe5f05a700b201f289"><code>3ed77af</code></a> chore: format</li>
<li><a href="https://github.com/remix-run/react-router/commit/e96962bc6159a2290632849b55872a3878753342"><code>e96962b</code></a> fix: remove stale unpkg field from react-router-dom (<a href="https://github.com/remix-run/react-router/tree/HEAD/packages/react-router-dom/issues/15075">#15075</a>)</li>
<li><a href="https://github.com/remix-run/react-router/commit/587d08fca6ca61e00f44c1eda95bf6e6a9ab76ef"><code>587d08f</code></a> Release v7.15.1 (<a href="https://github.com/remix-run/react-router/tree/HEAD/packages/react-router-dom/issues/15038">#15038</a>)</li>
<li>Additional commits viewable in <a href="https://github.com/remix-run/react-router/commits/react-router-dom@7.18.3/packages/react-router-dom">compare view</a></li>
</ul>
</details>
<br />


Dependabot will resolve any conflicts with this PR as long as you don't alter it yourself. You can also trigger a rebase manually by commenting `@dependabot rebase`.

[//]: # (dependabot-automerge-start)
[//]: # (dependabot-automerge-end)

---

<details>
<summary>Dependabot commands and options</summary>
<br />

You can trigger Dependabot actions by commenting on this PR:
- `@dependabot rebase` will rebase this PR
- `@dependabot recreate` will recreate this PR, overwriting any edits that have been made to it
- `@dependabot show <dependency name> ignore conditions` will show all of the ignore conditions of the specified dependency
- `@dependabot ignore this major version` will close this PR and stop Dependabot creating any more for this major version (unless you reopen the PR or upgrade to it yourself)
- `@dependabot ignore this minor version` will close this PR and stop Dependabot creating any more for this minor version (unless you reopen the PR or upgrade to it yourself)
- `@dependabot ignore this dependency` will close this PR and stop Dependabot creating any more for this dependency (unless you reopen the PR or upgrade to it yourself)
You can disable automated security fix PRs for this repo from the [Security Alerts page](https://github.com/nathanpond/AutoNate/network/alerts).

</details>

---

## archived-94 — Bump esbuild from 0.28.0 to 0.28.2

`MERGED (merged 2026-08-31)` · app/dependabot · opened 2026-08-31 · `dependabot/npm_and_yarn/esbuild-0.28.2` → `master`

Bumps [esbuild](https://github.com/evanw/esbuild) from 0.28.0 to 0.28.2.
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/evanw/esbuild/releases">esbuild's releases</a>.</em></p>
<blockquote>
<h2>v0.28.2</h2>
<ul>
<li>
<p>Fix tree shaking bug due to TypeScript import alias (<a href="https://redirect.github.com/evanw/esbuild/issues/4507">#4507</a>)</p>
<p>This release fixes a bug that could cause esbuild to incorrectly tree-shake imports that are used in a TypeScript type alias under certain circumstances. Affected code uses a TypeScript-specific <code>import</code> assignment and looks something like this:</p>
<pre lang="ts"><code>import Base from './dep.js';
import Alias = Base.SomeType;
</code></pre>
</li>
<li>
<p>Fix CSS minification bug involving <code>&amp;</code> (<a href="https://redirect.github.com/evanw/esbuild/issues/4497">#4497</a>)</p>
<p>This release fixes a bug where esbuild's CSS minifier incorrectly removed a <code>&amp;</code> when it was unsafe to do so. Here is an example:</p>
<pre lang="css"><code>/* Original code */
.a .b {
  &amp; .b:not(&amp; .c) {
    color: red;
  }
}
<p>/* Old output (with --minify) */<br />
.a .b{.b:not(&amp; .c){color:red}}</p>
<p>/* New output (with --minify) */<br />
.a .b{&amp; .b:not(&amp; .c){color:red}}<br />
</code></pre></p>
<p>This should match <code>&lt;span class=&quot;a&quot;&gt;&lt;span class=&quot;b&quot;&gt;&lt;span class=&quot;b&quot;&gt;yes&lt;/span&gt;&lt;/span&gt;&lt;/span&gt;</code> but not <code>&lt;span class=&quot;a&quot;&gt;&lt;span class=&quot;b&quot;&gt;no&lt;/span&gt;&lt;/span&gt;</code>. The old output incorrectly matched both.</p>
</li>
<li>
<p>Avoid overwriting input files without <code>--allow-overwrite</code> (<a href="https://redirect.github.com/evanw/esbuild/issues/4484">#4484</a>)</p>
<p>For example: <code>esbuild input.js --outfile=input.js</code> tells esbuild to overwrite <code>input.js</code> with the output of running esbuild on it. This was supposed to already be prevented by default, but it accidentally regressed in version 0.17.0 and apparently didn't have any test coverage. The error message was being printed but the input file was still being overwritten. Oops.</p>
<p>This release puts the original behavior back. With this release, esbuild should now actually avoid overwriting input files unless <code>--allow-overwrite</code> is explicitly present. This is done by not writing out any files when a build error is encountered.</p>
</li>
<li>
<p>Fix incorrect code generated when using top-level await (<a href="https://redirect.github.com/evanw/esbuild/issues/4498">#4498</a>)</p>
<p>Previously esbuild could generate code containing a syntax error in complex scenarios involving top-level await used in a dependency cycle. The problem was a missing <code>async</code> on one or more module wrapper closures. With this release, esbuild now uses a fixed-point iteration algorithm to correctly annotate all dependencies in the cycle as needing an <code>async</code> module wrapper.</p>
</li>
<li>
<p>Fix a minification bug with lowered logical assignment operators (<a href="https://redirect.github.com/evanw/esbuild/issues/4508">#4508</a>)</p>
<p>This release fixes a bug that could cause esbuild to generate incorrect code for logical assignment operators when lowering them to an older target environment. Specifically the lowering process requires duplicating the left-hand side, but esbuild incorrectly failed to count the duplicate as a new usage when the left-hand side is an identifier. That then caused the minifier to believe that the left-hand side was only used once and could attempt to incorrectly inline an initializer into the first usage. This bug has now been fixed:</p>
<pre lang="js"><code>// Original code
function foo() {
  let x
  bar(x ||= {})
</code></pre>
</li>
</ul>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Changelog</summary>
<p><em>Sourced from <a href="https://github.com/evanw/esbuild/blob/main/CHANGELOG.md">esbuild's changelog</a>.</em></p>
<blockquote>
<h2>0.28.2</h2>
<ul>
<li>
<p>Fix tree shaking bug due to TypeScript import alias (<a href="https://redirect.github.com/evanw/esbuild/issues/4507">#4507</a>)</p>
<p>This release fixes a bug that could cause esbuild to incorrectly tree-shake imports that are used in a TypeScript type alias under certain circumstances. Affected code uses a TypeScript-specific <code>import</code> assignment and looks something like this:</p>
<pre lang="ts"><code>import Base from './dep.js';
import Alias = Base.SomeType;
</code></pre>
</li>
<li>
<p>Fix CSS minification bug involving <code>&amp;</code> (<a href="https://redirect.github.com/evanw/esbuild/issues/4497">#4497</a>)</p>
<p>This release fixes a bug where esbuild's CSS minifier incorrectly removed a <code>&amp;</code> when it was unsafe to do so. Here is an example:</p>
<pre lang="css"><code>/* Original code */
.a .b {
  &amp; .b:not(&amp; .c) {
    color: red;
  }
}
<p>/* Old output (with --minify) */<br />
.a .b{.b:not(&amp; .c){color:red}}</p>
<p>/* New output (with --minify) */<br />
.a .b{&amp; .b:not(&amp; .c){color:red}}<br />
</code></pre></p>
<p>This should match <code>&lt;span class=&quot;a&quot;&gt;&lt;span class=&quot;b&quot;&gt;&lt;span class=&quot;b&quot;&gt;yes&lt;/span&gt;&lt;/span&gt;&lt;/span&gt;</code> but not <code>&lt;span class=&quot;a&quot;&gt;&lt;span class=&quot;b&quot;&gt;no&lt;/span&gt;&lt;/span&gt;</code>. The old output incorrectly matched both.</p>
</li>
<li>
<p>Avoid overwriting input files without <code>--allow-overwrite</code> (<a href="https://redirect.github.com/evanw/esbuild/issues/4484">#4484</a>)</p>
<p>For example: <code>esbuild input.js --outfile=input.js</code> tells esbuild to overwrite <code>input.js</code> with the output of running esbuild on it. This was supposed to already be prevented by default, but it accidentally regressed in version 0.17.0 and apparently didn't have any test coverage. The error message was being printed but the input file was still being overwritten. Oops.</p>
<p>This release puts the original behavior back. With this release, esbuild should now actually avoid overwriting input files unless <code>--allow-overwrite</code> is explicitly present. This is done by not writing out any files when a build error is encountered.</p>
</li>
<li>
<p>Fix incorrect code generated when using top-level await (<a href="https://redirect.github.com/evanw/esbuild/issues/4498">#4498</a>)</p>
<p>Previously esbuild could generate code containing a syntax error in complex scenarios involving top-level await used in a dependency cycle. The problem was a missing <code>async</code> on one or more module wrapper closures. With this release, esbuild now uses a fixed-point iteration algorithm to correctly annotate all dependencies in the cycle as needing an <code>async</code> module wrapper.</p>
</li>
<li>
<p>Fix a minification bug with lowered logical assignment operators (<a href="https://redirect.github.com/evanw/esbuild/issues/4508">#4508</a>)</p>
<p>This release fixes a bug that could cause esbuild to generate incorrect code for logical assignment operators when lowering them to an older target environment. Specifically the lowering process requires duplicating the left-hand side, but esbuild incorrectly failed to count the duplicate as a new usage when the left-hand side is an identifier. That then caused the minifier to believe that the left-hand side was only used once and could attempt to incorrectly inline an initializer into the first usage. This bug has now been fixed:</p>
<pre lang="js"><code>// Original code
function foo() {
  let x
</code></pre>
</li>
</ul>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/evanw/esbuild/commit/609683d892977362a0f99026cb74b96263d728a9"><code>609683d</code></a> publish 0.28.2 to npm</li>
<li><a href="https://github.com/evanw/esbuild/commit/11b1fe48df6859393d9469f323b5ebd17baaf989"><code>11b1fe4</code></a> add to release notes</li>
<li><a href="https://github.com/evanw/esbuild/commit/ab50d91559a27e54cd0a27a403389130ea10d97d"><code>ab50d91</code></a> css: fix green/blue channel swap in oklch gamut mapping (<a href="https://redirect.github.com/evanw/esbuild/issues/4488">#4488</a>)</li>
<li><a href="https://github.com/evanw/esbuild/commit/04627b6cf99b4a7491bebb0268173a7c77a85030"><code>04627b6</code></a> fix <a href="https://redirect.github.com/evanw/esbuild/issues/4498">#4498</a>: <code>async</code> TLA checks need a worklist</li>
<li><a href="https://github.com/evanw/esbuild/commit/5c15177a308c7224604058a769c4abf0a66b0a36"><code>5c15177</code></a> disable <code>gopls</code> in the <code>go</code> folder</li>
<li><a href="https://github.com/evanw/esbuild/commit/fc2ee9babc5a2e8ea7ec7c10dd5850b71f7cec7e"><code>fc2ee9b</code></a> css: adjust parser to allow <code>--foo: {...}</code></li>
<li><a href="https://github.com/evanw/esbuild/commit/209db54371e62ad1c50e12e56bb93c74c53b0408"><code>209db54</code></a> release notes for css nesting bugfix</li>
<li><a href="https://github.com/evanw/esbuild/commit/c625d31bf08a0647ec724bf76c7115f7aec55971"><code>c625d31</code></a> fix <a href="https://redirect.github.com/evanw/esbuild/issues/4497">#4497</a>: preserve nested ampersands during minification (<a href="https://redirect.github.com/evanw/esbuild/issues/4500">#4500</a>)</li>
<li><a href="https://github.com/evanw/esbuild/commit/34474e278528a60f58c959c0f422d2bfa6f6886d"><code>34474e2</code></a> better isolation of current part in js parser</li>
<li><a href="https://github.com/evanw/esbuild/commit/07f6e8c50677e0b41e5ed726c08b0ea200b14e5b"><code>07f6e8c</code></a> fix <a href="https://redirect.github.com/evanw/esbuild/issues/4507">#4507</a>: <code>import</code> assignment tree-shaking bug</li>
<li>Additional commits viewable in <a href="https://github.com/evanw/esbuild/compare/v0.28.0...v0.28.2">compare view</a></li>
</ul>
</details>
<br />


[![Dependabot compatibility score](https://dependabot-badges.githubapp.com/badges/compatibility_score?dependency-name=esbuild&package-manager=npm_and_yarn&previous-version=0.28.0&new-version=0.28.2)](https://docs.github.com/en/github/managing-security-vulnerabilities/about-dependabot-security-updates#about-compatibility-scores)

Dependabot will resolve any conflicts with this PR as long as you don't alter it yourself. You can also trigger a rebase manually by commenting `@dependabot rebase`.

[//]: # (dependabot-automerge-start)
[//]: # (dependabot-automerge-end)

---

<details>
<summary>Dependabot commands and options</summary>
<br />

You can trigger Dependabot actions by commenting on this PR:
- `@dependabot rebase` will rebase this PR
- `@dependabot recreate` will recreate this PR, overwriting any edits that have been made to it
- `@dependabot show <dependency name> ignore conditions` will show all of the ignore conditions of the specified dependency
- `@dependabot ignore this major version` will close this PR and stop Dependabot creating any more for this major version (unless you reopen the PR or upgrade to it yourself)
- `@dependabot ignore this minor version` will close this PR and stop Dependabot creating any more for this minor version (unless you reopen the PR or upgrade to it yourself)
- `@dependabot ignore this dependency` will close this PR and stop Dependabot creating any more for this dependency (unless you reopen the PR or upgrade to it yourself)


</details>

---

## archived-95 — Bump bpmn-js from 18.15.0 to 18.25.1

`MERGED (merged 2026-08-31)` · app/dependabot · opened 2026-08-31 · `dependabot/npm_and_yarn/bpmn-js-18.25.1` → `master`

Bumps [bpmn-js](https://github.com/bpmn-io/bpmn-js) from 18.15.0 to 18.25.1.
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/bpmn-io/bpmn-js/releases">bpmn-js's releases</a>.</em></p>
<blockquote>
<h2>v18.25.1</h2>
<h3>Changes</h3>
<ul>
<li>chore(CHANGELOG): update to v18.25.1  4efc9a94</li>
<li>chore(release): increase await published tries  8ecd8fb0</li>
<li>chore(release): remove redundant <code>$</code>  22d72ba7</li>
<li>chore(release): print time waited when await published failed  f1fdd79e</li>
<li>chore(release): extract await published interval into variable  5e7efd5c</li>
<li>fix(drilldown): make breadcrumb navigation keyboard accessible  1b9a3598</li>
<li>fix(drilldown): restore focus outline on drilldown button  deed26af</li>
<li>chore(CHANGELOG): update  cee7677d</li>
<li>fix(drilldown): give breadcrumbs a solid background  31cc4019</li>
</ul>
<hr />
<p><a href="https://github.com/bpmn-io/bpmn-js/compare/v18.25.0...v18.25.1">https://github.com/bpmn-io/bpmn-js/compare/v18.25.0...v18.25.1</a></p>
<h2>v18.25.0</h2>
<h3>Changes</h3>
<ul>
<li>chore(CHANGELOG): update to v18.25.0  d26ed8ae</li>
<li>feat: extract default element sizes into ElementSizeUtil  6423f3f2</li>
</ul>
<hr />
<p><a href="https://github.com/bpmn-io/bpmn-js/compare/v18.24.0...v18.25.0">https://github.com/bpmn-io/bpmn-js/compare/v18.24.0...v18.25.0</a></p>
<h2>v18.24.0</h2>
<ul>
<li>chore(CHANGELOG): update to v18.24.0  a2474a2f</li>
<li>deps: update to diagram-js@15.24.0  c4cf156d</li>
<li>chore: remove release-please  a744f837</li>
</ul>
<hr />
<p><a href="https://github.com/bpmn-io/bpmn-js/compare/v18.23.0...v18.24.0">https://github.com/bpmn-io/bpmn-js/compare/v18.23.0...v18.24.0</a></p>
<h2>v18.23.0</h2>
<h3>Changes</h3>
<ul>
<li>chore(CHANGELOG): update to v18.23.0  0c1cb075</li>
<li>Merge branch &amp;<a href="https://redirect.github.com/bpmn-io/bpmn-js/issues/39">#39</a>;main&amp;<a href="https://redirect.github.com/bpmn-io/bpmn-js/issues/39">#39</a>; into develop  3eb6180d</li>
<li>feat: tag elements contained in Group via categoryValueRef  52a2cfd6</li>
<li>feat(modeling): add category -&gt; categoryValue to groups on change  8bb463cd</li>
<li>test(modeling): clarify copied group categories  2dfbdd62</li>
<li>test(modeling): align spec with rest of behavior  6dc37018</li>
<li>feat(modeling): eagerly create group category values  ead71bee</li>
<li>refactor(modeling): extract category root element references  0395c36d</li>
<li>chore(modeling): improve readability  3a0d8dc7</li>
<li>fix(modeling): handle external label moves when updating lane refs  136c595f</li>
<li>chore(modeling): remove unused dependency  87007d0f</li>
</ul>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Changelog</summary>
<p><em>Sourced from <a href="https://github.com/bpmn-io/bpmn-js/blob/develop/CHANGELOG.md">bpmn-js's changelog</a>.</em></p>
<blockquote>
<h2>18.25.1</h2>
<ul>
<li><code>FIX</code>: make breadcrumps keyboard accessible (<a href="https://redirect.github.com/bpmn-io/bpmn-js/pull/2487">#2487</a>)</li>
<li><code>FIX</code>: give breadcrumps a background (<a href="https://redirect.github.com/bpmn-io/bpmn-js/pull/2487">#2487</a>, <a href="https://redirect.github.com/bpmn-io/bpmn-js/issues/2482">#2482</a>)</li>
</ul>
<h2>18.25.0</h2>
<ul>
<li><code>FEAT</code>: expose default element sizes via <code>lib/util/ElementSizeUtil</code> (<a href="https://redirect.github.com/bpmn-io/bpmn-js/issues/2485">#2485</a>)</li>
<li><code>FIX</code>: do not throw when getting default size of participant without DI (<a href="https://redirect.github.com/bpmn-io/bpmn-js/issues/2485">#2485</a>)</li>
</ul>
<h2>18.24.0</h2>
<ul>
<li><code>FEAT</code>: support tabs in popup menu (<a href="https://redirect.github.com/bpmn-io/diagram-js/pull/1096">bpmn-io/diagram-js#1096</a>)</li>
<li><code>FIX</code>: lay out docs link and drill-in chevron in popup menu as sibling actions (<a href="https://redirect.github.com/bpmn-io/diagram-js/pull/1088">bpmn-io/diagram-js#1088</a>)</li>
<li><code>DEPS</code>: update to <code>diagram-js@15.24.0</code></li>
</ul>
<h2>18.23.0</h2>
<ul>
<li><code>FEAT</code>: tag elements visually inside a group via <code>categoryValueRef</code> (<a href="https://redirect.github.com/bpmn-io/bpmn-js/pull/2469">#2469</a>)</li>
<li><code>FEAT</code>: eagerly create category value when creating or updating a group (<a href="https://redirect.github.com/bpmn-io/bpmn-js/pull/2469">#2469</a>)</li>
<li><code>FIX</code>: do not copy message flows without participants (<a href="https://redirect.github.com/bpmn-io/bpmn-js/issues/1902">#1902</a>, <a href="https://redirect.github.com/bpmn-io/bpmn-js/pull/2475">#2475</a>)</li>
<li><code>FIX</code>: ignore labels in <code>laneRef</code> updates (<a href="https://redirect.github.com/bpmn-io/bpmn-js/pull/2469">#2469</a>)</li>
<li><code>FIX</code>: do not copy message flows without participants (<a href="https://redirect.github.com/bpmn-io/bpmn-js/issues/1902">#1902</a>, <a href="https://redirect.github.com/bpmn-io/bpmn-js/pull/2475">#2475</a>)</li>
</ul>
<h2>18.22.1</h2>
<ul>
<li><code>FIX</code>: prevent clipped strokes in exported diagrams (<a href="https://redirect.github.com/bpmn-io/bpmn-js/pull/2476">#2476</a>)</li>
<li><code>DEPS</code>: update to <code>diagram-js@15.23.2</code></li>
</ul>
<h2>18.22.0</h2>
<ul>
<li><code>FEAT</code>: add shared popup entries list (<a href="https://redirect.github.com/bpmn-io/bpmn-js/pull/2463">#2463</a>)</li>
<li><code>FEAT</code>: make replace menu width configurable via css variable (<a href="https://redirect.github.com/bpmn-io/bpmn-js/pull/2463">#2463</a>)</li>
<li><code>FEAT</code>: complete direct editing on blur (<a href="https://redirect.github.com/bpmn-io/diagram-js-direct-editing/pull/74">bpmn-io/diagram-js-direct-editing#74</a>, <a href="https://redirect.github.com/bpmn-io/bpmn-js/issues/2327">#2327</a>, <a href="https://redirect.github.com/bpmn-io/bpmn-js/pull/2464">#2464</a>)</li>
<li><code>FEAT</code>: restore canvas focus in next render cycle (<a href="https://redirect.github.com/bpmn-io/diagram-js/pull/1081">bpmn-io/diagram-js#1081</a>)</li>
<li><code>FEAT</code>: use auto width with max value of 300px for popup menu (<a href="https://redirect.github.com/bpmn-io/diagram-js/pull/1078">bpmn-io/diagram-js#1078</a>)</li>
<li><code>DEPS</code>: update to <code>min-dash@5.1.0</code></li>
<li><code>DEPS</code>: update to <code>diagram-js@15.23.0</code></li>
<li><code>DEPS</code>: update to <code>diagram-js-direct-editing@3.5.1</code></li>
</ul>
<h2>18.21.0</h2>
<ul>
<li><code>FEAT</code>: improve aural interface / accessibility of popup menu (<a href="https://redirect.github.com/bpmn-io/diagram-js/pull/1059">bpmn-io/diagram-js#1059</a>, <a href="https://redirect.github.com/bpmn-io/diagram-js/issues/735">bpmn-io/diagram-js#735</a>)</li>
<li><code>FEAT</code>: allow <code>Canvas#findRoot</code> to find shared root for multiple elements (<a href="https://redirect.github.com/bpmn-io/diagram-js/pull/1075">bpmn-io/diagram-js#1075</a>)</li>
<li><code>FEAT</code>: support scrolling to multiple elements via <code>Canvas#scrollToElement</code> (<a href="https://redirect.github.com/bpmn-io/diagram-js/pull/1075">bpmn-io/diagram-js#1075</a>)</li>
<li><code>FIX</code>: improve mouse/keyboard interaction in popup menu (<a href="https://redirect.github.com/bpmn-io/diagram-js/pull/1060">bpmn-io/diagram-js#1060</a>)</li>
<li><code>DEPS</code>: update to <code>diagram-js@15.20.0</code></li>
</ul>
<h2>18.20.0</h2>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/bpmn-io/bpmn-js/commit/81e4d8988c2830422f18e6bd7600ace128a6990b"><code>81e4d89</code></a> 18.25.1</li>
<li><a href="https://github.com/bpmn-io/bpmn-js/commit/4efc9a947668d3114710b0d57a420a91eafafbb6"><code>4efc9a9</code></a> chore(CHANGELOG): update to v18.25.1</li>
<li><a href="https://github.com/bpmn-io/bpmn-js/commit/8ecd8fb0eeab126f28603a88392049ee4103685a"><code>8ecd8fb</code></a> chore(release): increase await published tries</li>
<li><a href="https://github.com/bpmn-io/bpmn-js/commit/22d72ba765cdf94ca00e400737ffc6ad9b35acff"><code>22d72ba</code></a> chore(release): remove redundant <code>$</code></li>
<li><a href="https://github.com/bpmn-io/bpmn-js/commit/f1fdd79e5fdec8786187afe4dfa0ff6ef6dc9b25"><code>f1fdd79</code></a> chore(release): print time waited when await published failed</li>
<li><a href="https://github.com/bpmn-io/bpmn-js/commit/5e7efd5cc915ec96ff3251cf66a71645a54324a7"><code>5e7efd5</code></a> chore(release): extract await published interval into variable</li>
<li><a href="https://github.com/bpmn-io/bpmn-js/commit/1b9a3598e63bf1043debfef191bd4d84404ed0df"><code>1b9a359</code></a> fix(drilldown): make breadcrumb navigation keyboard accessible</li>
<li><a href="https://github.com/bpmn-io/bpmn-js/commit/deed26af1817b7040c811420e506f85d16b05de2"><code>deed26a</code></a> fix(drilldown): restore focus outline on drilldown button</li>
<li><a href="https://github.com/bpmn-io/bpmn-js/commit/cee7677d73ac9dc72b75c9fa0df9ac4ad87d165b"><code>cee7677</code></a> chore(CHANGELOG): update</li>
<li><a href="https://github.com/bpmn-io/bpmn-js/commit/31cc40198c713bd7466ec434690c0499662c1daa"><code>31cc401</code></a> fix(drilldown): give breadcrumbs a solid background</li>
<li>Additional commits viewable in <a href="https://github.com/bpmn-io/bpmn-js/compare/v18.15.0...v18.25.1">compare view</a></li>
</ul>
</details>
<br />


[![Dependabot compatibility score](https://dependabot-badges.githubapp.com/badges/compatibility_score?dependency-name=bpmn-js&package-manager=npm_and_yarn&previous-version=18.15.0&new-version=18.25.1)](https://docs.github.com/en/github/managing-security-vulnerabilities/about-dependabot-security-updates#about-compatibility-scores)

Dependabot will resolve any conflicts with this PR as long as you don't alter it yourself. You can also trigger a rebase manually by commenting `@dependabot rebase`.

[//]: # (dependabot-automerge-start)
[//]: # (dependabot-automerge-end)

---

<details>
<summary>Dependabot commands and options</summary>
<br />

You can trigger Dependabot actions by commenting on this PR:
- `@dependabot rebase` will rebase this PR
- `@dependabot recreate` will recreate this PR, overwriting any edits that have been made to it
- `@dependabot show <dependency name> ignore conditions` will show all of the ignore conditions of the specified dependency
- `@dependabot ignore this major version` will close this PR and stop Dependabot creating any more for this major version (unless you reopen the PR or upgrade to it yourself)
- `@dependabot ignore this minor version` will close this PR and stop Dependabot creating any more for this minor version (unless you reopen the PR or upgrade to it yourself)
- `@dependabot ignore this dependency` will close this PR and stop Dependabot creating any more for this dependency (unless you reopen the PR or upgrade to it yourself)


</details>

---

## archived-96 — Bump the maven-minor-patch group across 1 directory with 10 updates

`MERGED (merged 2026-08-31)` · app/dependabot · opened 2026-08-31 · `dependabot/maven/flowable-extension/maven-minor-patch-3cdc32700b` → `master`

Bumps the maven-minor-patch group with 10 updates in the /flowable-extension directory:

| Package | From | To |
| --- | --- | --- |
| [org.springframework.boot:spring-boot-autoconfigure](https://github.com/spring-projects/spring-boot) | `4.0.2` | `4.1.1` |
| [org.springframework.boot:spring-boot](https://github.com/spring-projects/spring-boot) | `4.0.2` | `4.1.1` |
| [org.springframework.boot:spring-boot-actuator](https://github.com/spring-projects/spring-boot) | `4.0.2` | `4.1.1` |
| [org.springframework:spring-context](https://github.com/spring-projects/spring-framework) | `7.0.3` | `7.0.9` |
| [org.springframework:spring-web](https://github.com/spring-projects/spring-framework) | `7.0.3` | `7.0.9` |
| org.slf4j:slf4j-api | `2.0.17` | `2.0.18` |
| [com.fasterxml.jackson.core:jackson-databind](https://github.com/FasterXML/jackson-databind) | `2.20.2` | `2.22.2` |
| com.fasterxml.jackson.datatype:jackson-datatype-jsr310 | `2.20.2` | `2.22.2` |
| [org.apache.maven.plugins:maven-compiler-plugin](https://github.com/apache/maven-compiler-plugin) | `3.14.1` | `3.15.0` |
| [org.apache.maven.plugins:maven-surefire-plugin](https://github.com/apache/maven-surefire) | `3.5.4` | `3.5.6` |


Updates `org.springframework.boot:spring-boot-autoconfigure` from 4.0.2 to 4.1.1
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/spring-projects/spring-boot/releases">org.springframework.boot:spring-boot-autoconfigure's releases</a>.</em></p>
<blockquote>
<h2>v4.1.1</h2>
<h2>:warning: Attention Required</h2>
<ul>
<li>Spring Boot's Gradle plugin no longer automatically configures gRPC when the Protobuf plugin is applied. This behavior caused problems for those using Protobuf without gRPC. To opt in to the configuration of gRPC, configure the <code>protobuf</code> extension with the <code>grpc</code> plugin using an empty block. The Spring Boot Gradle plugin will then automatically configure the use of <code>protoc-gen-grpc-java</code> as before. <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50822">#50822</a></li>
</ul>
<h2>:lady_beetle: Bug Fixes</h2>
<ul>
<li>Kafka consumer-specific security protocol is not taken into account <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51369">#51369</a></li>
<li>Structured logging: a failed JSON encode corrupts the next log event written on the same thread <a href="https://redirect.github.com/spring-projects/spring-boot/pull/51156">#51156</a></li>
<li>Micrometer registries pin the application context <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51135">#51135</a></li>
<li>Temporary file is not deleted when ExportedImageTar construction fails <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51132">#51132</a></li>
<li>Metadata annotation processor ignores getter-level <code>@NestedConfigurationProperty</code> for records <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51098">#51098</a></li>
<li>spring-boot-h2-console pulls servlet-api as transitive dependency <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51095">#51095</a></li>
<li>PropertiesLauncher does not log nested archive paths <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51089">#51089</a></li>
<li>Methods that return the result of Map#remove are not declared with a <code>@Nullable</code> return type <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51087">#51087</a></li>
<li>NativeImageResourceProvider flattens Flyway migration paths in subdirectories <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50964">#50964</a></li>
<li>Fix ordering of Kotlinx Serialization CodecCustomizer <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50961">#50961</a></li>
<li>JarFile is not closed when finding main class from archive <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50959">#50959</a></li>
<li>Application-managed JUL bridge handler should only be removed if installed <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50950">#50950</a></li>
<li>CloudFoundry reactive auto-configuration should not require a WebClient.Builder bean to be defined <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50944">#50944</a></li>
<li>Context refresh fails on reactive Cloud Foundry when using Actuator without spring-boot-health <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50942">#50942</a></li>
<li>Resources are not cleaned up when resolving an image that is not yet present in the builder <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50941">#50941</a></li>
<li>GraphQlWebMvcAutoConfiguration should apply customizers in order <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50914">#50914</a></li>
<li>Auto-configured RedisMessageListenerContainer does not use virtual threads when spring.threads.virtual.enabled is true <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50884">#50884</a></li>
<li>Context refresh fails when using Actuator on Jersey without spring-boot-health <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50872">#50872</a></li>
<li>Context refresh fails on Cloud Foundry when using Actuator without spring-boot-health <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50871">#50871</a></li>
<li>IllegalStateException when binding properties to a <code>@Validated</code> class that contains a map whose value type is a wildcard <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50856">#50856</a></li>
<li>High number of connections due to Mongo health indicator <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50852">#50852</a></li>
<li>Inconsistent handling of empty string values of spring.security.oauth2.resourceserver.jwt issuer-uri and jwk-set-uri <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50849">#50849</a></li>
<li>Return type nullability of ApplicationContextAssert's getBean methods does not indicate that bean may be null <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50845">#50845</a></li>
<li>PropertiesWebClientHttpServiceGroupConfigurer has highest precedence, preventing other configurers from being ordered ahead of it <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50843">#50843</a></li>
<li>Exposing gRPC test server port should backoff if gRPC is not present <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50825">#50825</a></li>
<li>JpaBaseConfiguration#entityManagerConfiguration can cause a dependency loop on beans declaring AsyncTaskExecutor <a href="https://redirect.github.com/spring-projects/spring-boot/pull/50801">#50801</a></li>
<li>spring.grpc.server.health.include-overall-health is not taken into account <a href="https://redirect.github.com/spring-projects/spring-boot/pull/50799">#50799</a></li>
<li>Setting 'server.servlet.session.cookie.partitioned' to false still emits the 'Partitioned' cookie attribute <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50790">#50790</a></li>
<li>Managed version of Prometheus Client is not aligned with Micrometer's micrometer-registry-prometheus <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50780">#50780</a></li>
<li>Map properties bound from empty strings fail with ConverterNotFoundException <a href="https://redirect.github.com/spring-projects/spring-boot/pull/50773">#50773</a></li>
<li>Protobuf Common Protos should not be a managed dependency <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50772">#50772</a></li>
<li>An application that depends on spring-boot-security-oauth2-resource-server may fail to start with a ClassNotFoundException when Reactor is on the classpath but WebFlux is not <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50764">#50764</a></li>
<li>W3CHeaderParser's decoding is not compliant with RFC 3986 <a href="https://redirect.github.com/spring-projects/spring-boot/pull/50650">#50650</a></li>
</ul>
<h2>:notebook_with_decorative_cover: Documentation</h2>
<ul>
<li>Description of spring.graphql.websocket.connection-init-timeout does not render correctly in the reference guide <a href="https://redirect.github.com/spring-projects/spring-boot/pull/51348">#51348</a></li>
<li>spring.profiles.group should have a 'spring-profile-name' hint provider <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51284">#51284</a></li>
<li>Remove reference to removed InfluxDB auto-configuration <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51176">#51176</a></li>
<li>Use JacksonJsonSerde in Kafka Streams documentation <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51161">#51161</a></li>
<li>Document alternatives to HttpMessageConverters <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51129">#51129</a></li>
<li>Fix stale type reference for OTLP logging transport metadata <a href="https://redirect.github.com/spring-projects/spring-boot/pull/51119">#51119</a></li>
<li>Metadata for spring.test.mockmvc.htmlunit.url declares the wrong type <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51115">#51115</a></li>
</ul>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/spring-projects/spring-boot/commit/6fdf67ea1552691e932604d4bf67a5e08ff0b0ea"><code>6fdf67e</code></a> Release 4.1.1</li>
<li><a href="https://github.com/spring-projects/spring-boot/commit/fde599b28b40932e4ea6194399d89245923a01e7"><code>fde599b</code></a> Upgrade to Spring Pulsar 2.0.7</li>
<li><a href="https://github.com/spring-projects/spring-boot/commit/9daa58f7d98b82bf16febcb91943ce267c220a50"><code>9daa58f</code></a> Upgrade to Spring HATEOAS 3.1.2</li>
<li><a href="https://github.com/spring-projects/spring-boot/commit/353993ebc1d7b1ceccbb981b0439a42ba122a816"><code>353993e</code></a> Upgrade to Spring Data Bom 2026.0.1</li>
<li><a href="https://github.com/spring-projects/spring-boot/commit/24ba596bc4fa000614834016452435c834fdeda2"><code>24ba596</code></a> Upgrade to Spring Session 4.1.1</li>
<li><a href="https://github.com/spring-projects/spring-boot/commit/5cb5c294d1f4780e78d010a964049e47c074e4d6"><code>5cb5c29</code></a> Upgrade to Spring Security 7.1.1</li>
<li><a href="https://github.com/spring-projects/spring-boot/commit/4adc8eb130c4e0c688ed85316f385f4e04d47679"><code>4adc8eb</code></a> Upgrade to Spring LDAP 4.1.1</li>
<li><a href="https://github.com/spring-projects/spring-boot/commit/4d9c19cbc0589f57a7265052b507bf4b961082ac"><code>4d9c19c</code></a> Upgrade to Spring Kafka 4.1.1</li>
<li><a href="https://github.com/spring-projects/spring-boot/commit/f30f6120c4f6f8f873ac56cba478ae1f011c276c"><code>f30f612</code></a> Upgrade to Spring Integration 7.1.1</li>
<li><a href="https://github.com/spring-projects/spring-boot/commit/b930283d97e92eb24da1de3721cdd41625266dea"><code>b930283</code></a> Upgrade to Spring gRPC 1.1.1</li>
<li>Additional commits viewable in <a href="https://github.com/spring-projects/spring-boot/compare/v4.0.2...v4.1.1">compare view</a></li>
</ul>
</details>
<br />

Updates `org.springframework.boot:spring-boot` from 4.0.2 to 4.1.1
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/spring-projects/spring-boot/releases">org.springframework.boot:spring-boot's releases</a>.</em></p>
<blockquote>
<h2>v4.1.1</h2>
<h2>:warning: Attention Required</h2>
<ul>
<li>Spring Boot's Gradle plugin no longer automatically configures gRPC when the Protobuf plugin is applied. This behavior caused problems for those using Protobuf without gRPC. To opt in to the configuration of gRPC, configure the <code>protobuf</code> extension with the <code>grpc</code> plugin using an empty block. The Spring Boot Gradle plugin will then automatically configure the use of <code>protoc-gen-grpc-java</code> as before. <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50822">#50822</a></li>
</ul>
<h2>:lady_beetle: Bug Fixes</h2>
<ul>
<li>Kafka consumer-specific security protocol is not taken into account <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51369">#51369</a></li>
<li>Structured logging: a failed JSON encode corrupts the next log event written on the same thread <a href="https://redirect.github.com/spring-projects/spring-boot/pull/51156">#51156</a></li>
<li>Micrometer registries pin the application context <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51135">#51135</a></li>
<li>Temporary file is not deleted when ExportedImageTar construction fails <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51132">#51132</a></li>
<li>Metadata annotation processor ignores getter-level <code>@NestedConfigurationProperty</code> for records <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51098">#51098</a></li>
<li>spring-boot-h2-console pulls servlet-api as transitive dependency <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51095">#51095</a></li>
<li>PropertiesLauncher does not log nested archive paths <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51089">#51089</a></li>
<li>Methods that return the result of Map#remove are not declared with a <code>@Nullable</code> return type <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51087">#51087</a></li>
<li>NativeImageResourceProvider flattens Flyway migration paths in subdirectories <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50964">#50964</a></li>
<li>Fix ordering of Kotlinx Serialization CodecCustomizer <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50961">#50961</a></li>
<li>JarFile is not closed when finding main class from archive <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50959">#50959</a></li>
<li>Application-managed JUL bridge handler should only be removed if installed <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50950">#50950</a></li>
<li>CloudFoundry reactive auto-configuration should not require a WebClient.Builder bean to be defined <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50944">#50944</a></li>
<li>Context refresh fails on reactive Cloud Foundry when using Actuator without spring-boot-health <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50942">#50942</a></li>
<li>Resources are not cleaned up when resolving an image that is not yet present in the builder <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50941">#50941</a></li>
<li>GraphQlWebMvcAutoConfiguration should apply customizers in order <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50914">#50914</a></li>
<li>Auto-configured RedisMessageListenerContainer does not use virtual threads when spring.threads.virtual.enabled is true <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50884">#50884</a></li>
<li>Context refresh fails when using Actuator on Jersey without spring-boot-health <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50872">#50872</a></li>
<li>Context refresh fails on Cloud Foundry when using Actuator without spring-boot-health <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50871">#50871</a></li>
<li>IllegalStateException when binding properties to a <code>@Validated</code> class that contains a map whose value type is a wildcard <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50856">#50856</a></li>
<li>High number of connections due to Mongo health indicator <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50852">#50852</a></li>
<li>Inconsistent handling of empty string values of spring.security.oauth2.resourceserver.jwt issuer-uri and jwk-set-uri <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50849">#50849</a></li>
<li>Return type nullability of ApplicationContextAssert's getBean methods does not indicate that bean may be null <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50845">#50845</a></li>
<li>PropertiesWebClientHttpServiceGroupConfigurer has highest precedence, preventing other configurers from being ordered ahead of it <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50843">#50843</a></li>
<li>Exposing gRPC test server port should backoff if gRPC is not present <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50825">#50825</a></li>
<li>JpaBaseConfiguration#entityManagerConfiguration can cause a dependency loop on beans declaring AsyncTaskExecutor <a href="https://redirect.github.com/spring-projects/spring-boot/pull/50801">#50801</a></li>
<li>spring.grpc.server.health.include-overall-health is not taken into account <a href="https://redirect.github.com/spring-projects/spring-boot/pull/50799">#50799</a></li>
<li>Setting 'server.servlet.session.cookie.partitioned' to false still emits the 'Partitioned' cookie attribute <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50790">#50790</a></li>
<li>Managed version of Prometheus Client is not aligned with Micrometer's micrometer-registry-prometheus <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50780">#50780</a></li>
<li>Map properties bound from empty strings fail with ConverterNotFoundException <a href="https://redirect.github.com/spring-projects/spring-boot/pull/50773">#50773</a></li>
<li>Protobuf Common Protos should not be a managed dependency <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50772">#50772</a></li>
<li>An application that depends on spring-boot-security-oauth2-resource-server may fail to start with a ClassNotFoundException when Reactor is on the classpath but WebFlux is not <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50764">#50764</a></li>
<li>W3CHeaderParser's decoding is not compliant with RFC 3986 <a href="https://redirect.github.com/spring-projects/spring-boot/pull/50650">#50650</a></li>
</ul>
<h2>:notebook_with_decorative_cover: Documentation</h2>
<ul>
<li>Description of spring.graphql.websocket.connection-init-timeout does not render correctly in the reference guide <a href="https://redirect.github.com/spring-projects/spring-boot/pull/51348">#51348</a></li>
<li>spring.profiles.group should have a 'spring-profile-name' hint provider <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51284">#51284</a></li>
<li>Remove reference to removed InfluxDB auto-configuration <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51176">#51176</a></li>
<li>Use JacksonJsonSerde in Kafka Streams documentation <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51161">#51161</a></li>
<li>Document alternatives to HttpMessageConverters <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51129">#51129</a></li>
<li>Fix stale type reference for OTLP logging transport metadata <a href="https://redirect.github.com/spring-projects/spring-boot/pull/51119">#51119</a></li>
<li>Metadata for spring.test.mockmvc.htmlunit.url declares the wrong type <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51115">#51115</a></li>
</ul>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/spring-projects/spring-boot/commit/6fdf67ea1552691e932604d4bf67a5e08ff0b0ea"><code>6fdf67e</code></a> Release 4.1.1</li>
<li><a href="https://github.com/spring-projects/spring-boot/commit/fde599b28b40932e4ea6194399d89245923a01e7"><code>fde599b</code></a> Upgrade to Spring Pulsar 2.0.7</li>
<li><a href="https://github.com/spring-projects/spring-boot/commit/9daa58f7d98b82bf16febcb91943ce267c220a50"><code>9daa58f</code></a> Upgrade to Spring HATEOAS 3.1.2</li>
<li><a href="https://github.com/spring-projects/spring-boot/commit/353993ebc1d7b1ceccbb981b0439a42ba122a816"><code>353993e</code></a> Upgrade to Spring Data Bom 2026.0.1</li>
<li><a href="https://github.com/spring-projects/spring-boot/commit/24ba596bc4fa000614834016452435c834fdeda2"><code>24ba596</code></a> Upgrade to Spring Session 4.1.1</li>
<li><a href="https://github.com/spring-projects/spring-boot/commit/5cb5c294d1f4780e78d010a964049e47c074e4d6"><code>5cb5c29</code></a> Upgrade to Spring Security 7.1.1</li>
<li><a href="https://github.com/spring-projects/spring-boot/commit/4adc8eb130c4e0c688ed85316f385f4e04d47679"><code>4adc8eb</code></a> Upgrade to Spring LDAP 4.1.1</li>
<li><a href="https://github.com/spring-projects/spring-boot/commit/4d9c19cbc0589f57a7265052b507bf4b961082ac"><code>4d9c19c</code></a> Upgrade to Spring Kafka 4.1.1</li>
<li><a href="https://github.com/spring-projects/spring-boot/commit/f30f6120c4f6f8f873ac56cba478ae1f011c276c"><code>f30f612</code></a> Upgrade to Spring Integration 7.1.1</li>
<li><a href="https://github.com/spring-projects/spring-boot/commit/b930283d97e92eb24da1de3721cdd41625266dea"><code>b930283</code></a> Upgrade to Spring gRPC 1.1.1</li>
<li>Additional commits viewable in <a href="https://github.com/spring-projects/spring-boot/compare/v4.0.2...v4.1.1">compare view</a></li>
</ul>
</details>
<br />

Updates `org.springframework.boot:spring-boot-actuator` from 4.0.2 to 4.1.1
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/spring-projects/spring-boot/releases">org.springframework.boot:spring-boot-actuator's releases</a>.</em></p>
<blockquote>
<h2>v4.1.1</h2>
<h2>:warning: Attention Required</h2>
<ul>
<li>Spring Boot's Gradle plugin no longer automatically configures gRPC when the Protobuf plugin is applied. This behavior caused problems for those using Protobuf without gRPC. To opt in to the configuration of gRPC, configure the <code>protobuf</code> extension with the <code>grpc</code> plugin using an empty block. The Spring Boot Gradle plugin will then automatically configure the use of <code>protoc-gen-grpc-java</code> as before. <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50822">#50822</a></li>
</ul>
<h2>:lady_beetle: Bug Fixes</h2>
<ul>
<li>Kafka consumer-specific security protocol is not taken into account <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51369">#51369</a></li>
<li>Structured logging: a failed JSON encode corrupts the next log event written on the same thread <a href="https://redirect.github.com/spring-projects/spring-boot/pull/51156">#51156</a></li>
<li>Micrometer registries pin the application context <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51135">#51135</a></li>
<li>Temporary file is not deleted when ExportedImageTar construction fails <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51132">#51132</a></li>
<li>Metadata annotation processor ignores getter-level <code>@NestedConfigurationProperty</code> for records <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51098">#51098</a></li>
<li>spring-boot-h2-console pulls servlet-api as transitive dependency <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51095">#51095</a></li>
<li>PropertiesLauncher does not log nested archive paths <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51089">#51089</a></li>
<li>Methods that return the result of Map#remove are not declared with a <code>@Nullable</code> return type <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51087">#51087</a></li>
<li>NativeImageResourceProvider flattens Flyway migration paths in subdirectories <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50964">#50964</a></li>
<li>Fix ordering of Kotlinx Serialization CodecCustomizer <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50961">#50961</a></li>
<li>JarFile is not closed when finding main class from archive <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50959">#50959</a></li>
<li>Application-managed JUL bridge handler should only be removed if installed <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50950">#50950</a></li>
<li>CloudFoundry reactive auto-configuration should not require a WebClient.Builder bean to be defined <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50944">#50944</a></li>
<li>Context refresh fails on reactive Cloud Foundry when using Actuator without spring-boot-health <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50942">#50942</a></li>
<li>Resources are not cleaned up when resolving an image that is not yet present in the builder <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50941">#50941</a></li>
<li>GraphQlWebMvcAutoConfiguration should apply customizers in order <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50914">#50914</a></li>
<li>Auto-configured RedisMessageListenerContainer does not use virtual threads when spring.threads.virtual.enabled is true <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50884">#50884</a></li>
<li>Context refresh fails when using Actuator on Jersey without spring-boot-health <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50872">#50872</a></li>
<li>Context refresh fails on Cloud Foundry when using Actuator without spring-boot-health <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50871">#50871</a></li>
<li>IllegalStateException when binding properties to a <code>@Validated</code> class that contains a map whose value type is a wildcard <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50856">#50856</a></li>
<li>High number of connections due to Mongo health indicator <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50852">#50852</a></li>
<li>Inconsistent handling of empty string values of spring.security.oauth2.resourceserver.jwt issuer-uri and jwk-set-uri <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50849">#50849</a></li>
<li>Return type nullability of ApplicationContextAssert's getBean methods does not indicate that bean may be null <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50845">#50845</a></li>
<li>PropertiesWebClientHttpServiceGroupConfigurer has highest precedence, preventing other configurers from being ordered ahead of it <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50843">#50843</a></li>
<li>Exposing gRPC test server port should backoff if gRPC is not present <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50825">#50825</a></li>
<li>JpaBaseConfiguration#entityManagerConfiguration can cause a dependency loop on beans declaring AsyncTaskExecutor <a href="https://redirect.github.com/spring-projects/spring-boot/pull/50801">#50801</a></li>
<li>spring.grpc.server.health.include-overall-health is not taken into account <a href="https://redirect.github.com/spring-projects/spring-boot/pull/50799">#50799</a></li>
<li>Setting 'server.servlet.session.cookie.partitioned' to false still emits the 'Partitioned' cookie attribute <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50790">#50790</a></li>
<li>Managed version of Prometheus Client is not aligned with Micrometer's micrometer-registry-prometheus <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50780">#50780</a></li>
<li>Map properties bound from empty strings fail with ConverterNotFoundException <a href="https://redirect.github.com/spring-projects/spring-boot/pull/50773">#50773</a></li>
<li>Protobuf Common Protos should not be a managed dependency <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50772">#50772</a></li>
<li>An application that depends on spring-boot-security-oauth2-resource-server may fail to start with a ClassNotFoundException when Reactor is on the classpath but WebFlux is not <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50764">#50764</a></li>
<li>W3CHeaderParser's decoding is not compliant with RFC 3986 <a href="https://redirect.github.com/spring-projects/spring-boot/pull/50650">#50650</a></li>
</ul>
<h2>:notebook_with_decorative_cover: Documentation</h2>
<ul>
<li>Description of spring.graphql.websocket.connection-init-timeout does not render correctly in the reference guide <a href="https://redirect.github.com/spring-projects/spring-boot/pull/51348">#51348</a></li>
<li>spring.profiles.group should have a 'spring-profile-name' hint provider <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51284">#51284</a></li>
<li>Remove reference to removed InfluxDB auto-configuration <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51176">#51176</a></li>
<li>Use JacksonJsonSerde in Kafka Streams documentation <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51161">#51161</a></li>
<li>Document alternatives to HttpMessageConverters <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51129">#51129</a></li>
<li>Fix stale type reference for OTLP logging transport metadata <a href="https://redirect.github.com/spring-projects/spring-boot/pull/51119">#51119</a></li>
<li>Metadata for spring.test.mockmvc.htmlunit.url declares the wrong type <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51115">#51115</a></li>
</ul>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/spring-projects/spring-boot/commit/6fdf67ea1552691e932604d4bf67a5e08ff0b0ea"><code>6fdf67e</code></a> Release 4.1.1</li>
<li><a href="https://github.com/spring-projects/spring-boot/commit/fde599b28b40932e4ea6194399d89245923a01e7"><code>fde599b</code></a> Upgrade to Spring Pulsar 2.0.7</li>
<li><a href="https://github.com/spring-projects/spring-boot/commit/9daa58f7d98b82bf16febcb91943ce267c220a50"><code>9daa58f</code></a> Upgrade to Spring HATEOAS 3.1.2</li>
<li><a href="https://github.com/spring-projects/spring-boot/commit/353993ebc1d7b1ceccbb981b0439a42ba122a816"><code>353993e</code></a> Upgrade to Spring Data Bom 2026.0.1</li>
<li><a href="https://github.com/spring-projects/spring-boot/commit/24ba596bc4fa000614834016452435c834fdeda2"><code>24ba596</code></a> Upgrade to Spring Session 4.1.1</li>
<li><a href="https://github.com/spring-projects/spring-boot/commit/5cb5c294d1f4780e78d010a964049e47c074e4d6"><code>5cb5c29</code></a> Upgrade to Spring Security 7.1.1</li>
<li><a href="https://github.com/spring-projects/spring-boot/commit/4adc8eb130c4e0c688ed85316f385f4e04d47679"><code>4adc8eb</code></a> Upgrade to Spring LDAP 4.1.1</li>
<li><a href="https://github.com/spring-projects/spring-boot/commit/4d9c19cbc0589f57a7265052b507bf4b961082ac"><code>4d9c19c</code></a> Upgrade to Spring Kafka 4.1.1</li>
<li><a href="https://github.com/spring-projects/spring-boot/commit/f30f6120c4f6f8f873ac56cba478ae1f011c276c"><code>f30f612</code></a> Upgrade to Spring Integration 7.1.1</li>
<li><a href="https://github.com/spring-projects/spring-boot/commit/b930283d97e92eb24da1de3721cdd41625266dea"><code>b930283</code></a> Upgrade to Spring gRPC 1.1.1</li>
<li>Additional commits viewable in <a href="https://github.com/spring-projects/spring-boot/compare/v4.0.2...v4.1.1">compare view</a></li>
</ul>
</details>
<br />

Updates `org.springframework.boot:spring-boot` from 4.0.2 to 4.1.1
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/spring-projects/spring-boot/releases">org.springframework.boot:spring-boot's releases</a>.</em></p>
<blockquote>
<h2>v4.1.1</h2>
<h2>:warning: Attention Required</h2>
<ul>
<li>Spring Boot's Gradle plugin no longer automatically configures gRPC when the Protobuf plugin is applied. This behavior caused problems for those using Protobuf without gRPC. To opt in to the configuration of gRPC, configure the <code>protobuf</code> extension with the <code>grpc</code> plugin using an empty block. The Spring Boot Gradle plugin will then automatically configure the use of <code>protoc-gen-grpc-java</code> as before. <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50822">#50822</a></li>
</ul>
<h2>:lady_beetle: Bug Fixes</h2>
<ul>
<li>Kafka consumer-specific security protocol is not taken into account <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51369">#51369</a></li>
<li>Structured logging: a failed JSON encode corrupts the next log event written on the same thread <a href="https://redirect.github.com/spring-projects/spring-boot/pull/51156">#51156</a></li>
<li>Micrometer registries pin the application context <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51135">#51135</a></li>
<li>Temporary file is not deleted when ExportedImageTar construction fails <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51132">#51132</a></li>
<li>Metadata annotation processor ignores getter-level <code>@NestedConfigurationProperty</code> for records <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51098">#51098</a></li>
<li>spring-boot-h2-console pulls servlet-api as transitive dependency <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51095">#51095</a></li>
<li>PropertiesLauncher does not log nested archive paths <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51089">#51089</a></li>
<li>Methods that return the result of Map#remove are not declared with a <code>@Nullable</code> return type <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51087">#51087</a></li>
<li>NativeImageResourceProvider flattens Flyway migration paths in subdirectories <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50964">#50964</a></li>
<li>Fix ordering of Kotlinx Serialization CodecCustomizer <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50961">#50961</a></li>
<li>JarFile is not closed when finding main class from archive <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50959">#50959</a></li>
<li>Application-managed JUL bridge handler should only be removed if installed <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50950">#50950</a></li>
<li>CloudFoundry reactive auto-configuration should not require a WebClient.Builder bean to be defined <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50944">#50944</a></li>
<li>Context refresh fails on reactive Cloud Foundry when using Actuator without spring-boot-health <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50942">#50942</a></li>
<li>Resources are not cleaned up when resolving an image that is not yet present in the builder <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50941">#50941</a></li>
<li>GraphQlWebMvcAutoConfiguration should apply customizers in order <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50914">#50914</a></li>
<li>Auto-configured RedisMessageListenerContainer does not use virtual threads when spring.threads.virtual.enabled is true <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50884">#50884</a></li>
<li>Context refresh fails when using Actuator on Jersey without spring-boot-health <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50872">#50872</a></li>
<li>Context refresh fails on Cloud Foundry when using Actuator without spring-boot-health <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50871">#50871</a></li>
<li>IllegalStateException when binding properties to a <code>@Validated</code> class that contains a map whose value type is a wildcard <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50856">#50856</a></li>
<li>High number of connections due to Mongo health indicator <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50852">#50852</a></li>
<li>Inconsistent handling of empty string values of spring.security.oauth2.resourceserver.jwt issuer-uri and jwk-set-uri <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50849">#50849</a></li>
<li>Return type nullability of ApplicationContextAssert's getBean methods does not indicate that bean may be null <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50845">#50845</a></li>
<li>PropertiesWebClientHttpServiceGroupConfigurer has highest precedence, preventing other configurers from being ordered ahead of it <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50843">#50843</a></li>
<li>Exposing gRPC test server port should backoff if gRPC is not present <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50825">#50825</a></li>
<li>JpaBaseConfiguration#entityManagerConfiguration can cause a dependency loop on beans declaring AsyncTaskExecutor <a href="https://redirect.github.com/spring-projects/spring-boot/pull/50801">#50801</a></li>
<li>spring.grpc.server.health.include-overall-health is not taken into account <a href="https://redirect.github.com/spring-projects/spring-boot/pull/50799">#50799</a></li>
<li>Setting 'server.servlet.session.cookie.partitioned' to false still emits the 'Partitioned' cookie attribute <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50790">#50790</a></li>
<li>Managed version of Prometheus Client is not aligned with Micrometer's micrometer-registry-prometheus <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50780">#50780</a></li>
<li>Map properties bound from empty strings fail with ConverterNotFoundException <a href="https://redirect.github.com/spring-projects/spring-boot/pull/50773">#50773</a></li>
<li>Protobuf Common Protos should not be a managed dependency <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50772">#50772</a></li>
<li>An application that depends on spring-boot-security-oauth2-resource-server may fail to start with a ClassNotFoundException when Reactor is on the classpath but WebFlux is not <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50764">#50764</a></li>
<li>W3CHeaderParser's decoding is not compliant with RFC 3986 <a href="https://redirect.github.com/spring-projects/spring-boot/pull/50650">#50650</a></li>
</ul>
<h2>:notebook_with_decorative_cover: Documentation</h2>
<ul>
<li>Description of spring.graphql.websocket.connection-init-timeout does not render correctly in the reference guide <a href="https://redirect.github.com/spring-projects/spring-boot/pull/51348">#51348</a></li>
<li>spring.profiles.group should have a 'spring-profile-name' hint provider <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51284">#51284</a></li>
<li>Remove reference to removed InfluxDB auto-configuration <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51176">#51176</a></li>
<li>Use JacksonJsonSerde in Kafka Streams documentation <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51161">#51161</a></li>
<li>Document alternatives to HttpMessageConverters <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51129">#51129</a></li>
<li>Fix stale type reference for OTLP logging transport metadata <a href="https://redirect.github.com/spring-projects/spring-boot/pull/51119">#51119</a></li>
<li>Metadata for spring.test.mockmvc.htmlunit.url declares the wrong type <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51115">#51115</a></li>
</ul>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/spring-projects/spring-boot/commit/6fdf67ea1552691e932604d4bf67a5e08ff0b0ea"><code>6fdf67e</code></a> Release 4.1.1</li>
<li><a href="https://github.com/spring-projects/spring-boot/commit/fde599b28b40932e4ea6194399d89245923a01e7"><code>fde599b</code></a> Upgrade to Spring Pulsar 2.0.7</li>
<li><a href="https://github.com/spring-projects/spring-boot/commit/9daa58f7d98b82bf16febcb91943ce267c220a50"><code>9daa58f</code></a> Upgrade to Spring HATEOAS 3.1.2</li>
<li><a href="https://github.com/spring-projects/spring-boot/commit/353993ebc1d7b1ceccbb981b0439a42ba122a816"><code>353993e</code></a> Upgrade to Spring Data Bom 2026.0.1</li>
<li><a href="https://github.com/spring-projects/spring-boot/commit/24ba596bc4fa000614834016452435c834fdeda2"><code>24ba596</code></a> Upgrade to Spring Session 4.1.1</li>
<li><a href="https://github.com/spring-projects/spring-boot/commit/5cb5c294d1f4780e78d010a964049e47c074e4d6"><code>5cb5c29</code></a> Upgrade to Spring Security 7.1.1</li>
<li><a href="https://github.com/spring-projects/spring-boot/commit/4adc8eb130c4e0c688ed85316f385f4e04d47679"><code>4adc8eb</code></a> Upgrade to Spring LDAP 4.1.1</li>
<li><a href="https://github.com/spring-projects/spring-boot/commit/4d9c19cbc0589f57a7265052b507bf4b961082ac"><code>4d9c19c</code></a> Upgrade to Spring Kafka 4.1.1</li>
<li><a href="https://github.com/spring-projects/spring-boot/commit/f30f6120c4f6f8f873ac56cba478ae1f011c276c"><code>f30f612</code></a> Upgrade to Spring Integration 7.1.1</li>
<li><a href="https://github.com/spring-projects/spring-boot/commit/b930283d97e92eb24da1de3721cdd41625266dea"><code>b930283</code></a> Upgrade to Spring gRPC 1.1.1</li>
<li>Additional commits viewable in <a href="https://github.com/spring-projects/spring-boot/compare/v4.0.2...v4.1.1">compare view</a></li>
</ul>
</details>
<br />

Updates `org.springframework.boot:spring-boot-actuator` from 4.0.2 to 4.1.1
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/spring-projects/spring-boot/releases">org.springframework.boot:spring-boot-actuator's releases</a>.</em></p>
<blockquote>
<h2>v4.1.1</h2>
<h2>:warning: Attention Required</h2>
<ul>
<li>Spring Boot's Gradle plugin no longer automatically configures gRPC when the Protobuf plugin is applied. This behavior caused problems for those using Protobuf without gRPC. To opt in to the configuration of gRPC, configure the <code>protobuf</code> extension with the <code>grpc</code> plugin using an empty block. The Spring Boot Gradle plugin will then automatically configure the use of <code>protoc-gen-grpc-java</code> as before. <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50822">#50822</a></li>
</ul>
<h2>:lady_beetle: Bug Fixes</h2>
<ul>
<li>Kafka consumer-specific security protocol is not taken into account <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51369">#51369</a></li>
<li>Structured logging: a failed JSON encode corrupts the next log event written on the same thread <a href="https://redirect.github.com/spring-projects/spring-boot/pull/51156">#51156</a></li>
<li>Micrometer registries pin the application context <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51135">#51135</a></li>
<li>Temporary file is not deleted when ExportedImageTar construction fails <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51132">#51132</a></li>
<li>Metadata annotation processor ignores getter-level <code>@NestedConfigurationProperty</code> for records <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51098">#51098</a></li>
<li>spring-boot-h2-console pulls servlet-api as transitive dependency <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51095">#51095</a></li>
<li>PropertiesLauncher does not log nested archive paths <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51089">#51089</a></li>
<li>Methods that return the result of Map#remove are not declared with a <code>@Nullable</code> return type <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51087">#51087</a></li>
<li>NativeImageResourceProvider flattens Flyway migration paths in subdirectories <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50964">#50964</a></li>
<li>Fix ordering of Kotlinx Serialization CodecCustomizer <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50961">#50961</a></li>
<li>JarFile is not closed when finding main class from archive <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50959">#50959</a></li>
<li>Application-managed JUL bridge handler should only be removed if installed <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50950">#50950</a></li>
<li>CloudFoundry reactive auto-configuration should not require a WebClient.Builder bean to be defined <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50944">#50944</a></li>
<li>Context refresh fails on reactive Cloud Foundry when using Actuator without spring-boot-health <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50942">#50942</a></li>
<li>Resources are not cleaned up when resolving an image that is not yet present in the builder <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50941">#50941</a></li>
<li>GraphQlWebMvcAutoConfiguration should apply customizers in order <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50914">#50914</a></li>
<li>Auto-configured RedisMessageListenerContainer does not use virtual threads when spring.threads.virtual.enabled is true <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50884">#50884</a></li>
<li>Context refresh fails when using Actuator on Jersey without spring-boot-health <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50872">#50872</a></li>
<li>Context refresh fails on Cloud Foundry when using Actuator without spring-boot-health <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50871">#50871</a></li>
<li>IllegalStateException when binding properties to a <code>@Validated</code> class that contains a map whose value type is a wildcard <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50856">#50856</a></li>
<li>High number of connections due to Mongo health indicator <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50852">#50852</a></li>
<li>Inconsistent handling of empty string values of spring.security.oauth2.resourceserver.jwt issuer-uri and jwk-set-uri <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50849">#50849</a></li>
<li>Return type nullability of ApplicationContextAssert's getBean methods does not indicate that bean may be null <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50845">#50845</a></li>
<li>PropertiesWebClientHttpServiceGroupConfigurer has highest precedence, preventing other configurers from being ordered ahead of it <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50843">#50843</a></li>
<li>Exposing gRPC test server port should backoff if gRPC is not present <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50825">#50825</a></li>
<li>JpaBaseConfiguration#entityManagerConfiguration can cause a dependency loop on beans declaring AsyncTaskExecutor <a href="https://redirect.github.com/spring-projects/spring-boot/pull/50801">#50801</a></li>
<li>spring.grpc.server.health.include-overall-health is not taken into account <a href="https://redirect.github.com/spring-projects/spring-boot/pull/50799">#50799</a></li>
<li>Setting 'server.servlet.session.cookie.partitioned' to false still emits the 'Partitioned' cookie attribute <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50790">#50790</a></li>
<li>Managed version of Prometheus Client is not aligned with Micrometer's micrometer-registry-prometheus <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50780">#50780</a></li>
<li>Map properties bound from empty strings fail with ConverterNotFoundException <a href="https://redirect.github.com/spring-projects/spring-boot/pull/50773">#50773</a></li>
<li>Protobuf Common Protos should not be a managed dependency <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50772">#50772</a></li>
<li>An application that depends on spring-boot-security-oauth2-resource-server may fail to start with a ClassNotFoundException when Reactor is on the classpath but WebFlux is not <a href="https://redirect.github.com/spring-projects/spring-boot/issues/50764">#50764</a></li>
<li>W3CHeaderParser's decoding is not compliant with RFC 3986 <a href="https://redirect.github.com/spring-projects/spring-boot/pull/50650">#50650</a></li>
</ul>
<h2>:notebook_with_decorative_cover: Documentation</h2>
<ul>
<li>Description of spring.graphql.websocket.connection-init-timeout does not render correctly in the reference guide <a href="https://redirect.github.com/spring-projects/spring-boot/pull/51348">#51348</a></li>
<li>spring.profiles.group should have a 'spring-profile-name' hint provider <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51284">#51284</a></li>
<li>Remove reference to removed InfluxDB auto-configuration <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51176">#51176</a></li>
<li>Use JacksonJsonSerde in Kafka Streams documentation <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51161">#51161</a></li>
<li>Document alternatives to HttpMessageConverters <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51129">#51129</a></li>
<li>Fix stale type reference for OTLP logging transport metadata <a href="https://redirect.github.com/spring-projects/spring-boot/pull/51119">#51119</a></li>
<li>Metadata for spring.test.mockmvc.htmlunit.url declares the wrong type <a href="https://redirect.github.com/spring-projects/spring-boot/issues/51115">#51115</a></li>
</ul>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/spring-projects/spring-boot/commit/6fdf67ea1552691e932604d4bf67a5e08ff0b0ea"><code>6fdf67e</code></a> Release 4.1.1</li>
<li><a href="https://github.com/spring-projects/spring-boot/commit/fde599b28b40932e4ea6194399d89245923a01e7"><code>fde599b</code></a> Upgrade to Spring Pulsar 2.0.7</li>
<li><a href="https://github.com/spring-projects/spring-boot/commit/9daa58f7d98b82bf16febcb91943ce267c220a50"><code>9daa58f</code></a> Upgrade to Spring HATEOAS 3.1.2</li>
<li><a href="https://github.com/spring-projects/spring-boot/commit/353993ebc1d7b1ceccbb981b0439a42ba122a816"><code>353993e</code></a> Upgrade to Spring Data Bom 2026.0.1</li>
<li><a href="https://github.com/spring-projects/spring-boot/commit/24ba596bc4fa000614834016452435c834fdeda2"><code>24ba596</code></a> Upgrade to Spring Session 4.1.1</li>
<li><a href="https://github.com/spring-projects/spring-boot/commit/5cb5c294d1f4780e78d010a964049e47c074e4d6"><code>5cb5c29</code></a> Upgrade to Spring Security 7.1.1</li>
<li><a href="https://github.com/spring-projects/spring-boot/commit/4adc8eb130c4e0c688ed85316f385f4e04d47679"><code>4adc8eb</code></a> Upgrade to Spring LDAP 4.1.1</li>
<li><a href="https://github.com/spring-projects/spring-boot/commit/4d9c19cbc0589f57a7265052b507bf4b961082ac"><code>4d9c19c</code></a> Upgrade to Spring Kafka 4.1.1</li>
<li><a href="https://github.com/spring-projects/spring-boot/commit/f30f6120c4f6f8f873ac56cba478ae1f011c276c"><code>f30f612</code></a> Upgrade to Spring Integration 7.1.1</li>
<li><a href="https://github.com/spring-projects/spring-boot/commit/b930283d97e92eb24da1de3721cdd41625266dea"><code>b930283</code></a> Upgrade to Spring gRPC 1.1.1</li>
<li>Additional commits viewable in <a href="https://github.com/spring-projects/spring-boot/compare/v4.0.2...v4.1.1">compare view</a></li>
</ul>
</details>
<br />

Updates `org.springframework:spring-context` from 7.0.3 to 7.0.9
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/spring-projects/spring-framework/releases">org.springframework:spring-context's releases</a>.</em></p>
<blockquote>
<h2>v7.0.9</h2>
<h2>:warning: Attention Required</h2>
<ul>
<li>In Spring Framework 7.0.9, <code>ForwardedHeaderFilter</code> (Spring MVC) and <code>ForwardedHeaderTransformer</code> (WebFlux) each provide a boolean constructor argument whether to use the standard &quot;Forwarded&quot; header or the &quot;X-Forwarded&quot; alternative headers. A separate property turns on and off use of &quot;X-Forwarded-Prefix&quot;. While the default constructor preserves the existing behavior, we recommend to use the new constructor to explicitly specify which forwarded headers to use to make the processing more deterministic and aligned with what is expected from the proxy. Please, see the updated <a href="https://docs.spring.io/spring-framework/reference/7.0.9-SNAPSHOT/web/webmvc/filters.html#filters-forwarded-headers-security">Security Considerations</a> section for details. In 7.1 with <a href="https://redirect.github.com/spring-projects/spring-framework/issues/37072">#37072</a> the default constructor is deprecated and marked for removal. <a href="https://redirect.github.com/spring-projects/spring-framework/issues/37090">#37090</a></li>
<li>In Spring Framework 7.0.9, <code>SimpleEvaluationContext</code> no longer supports expression compilation by default, regardless of the compiler mode configured via <code>SpelParserConfiguration</code> or the <code>spring.expression.compiler.mode</code> system property or Spring property. Applications that intentionally use <code>SimpleEvaluationContext</code> with trusted expressions and require compilation for performance reasons can opt in by calling <code>withCompilationSupported()</code> on the <code>SimpleEvaluationContext</code> builder. Care should be taken when opting in to compilation, as doing so removes the safety guards applied during interpreted evaluation. <a href="https://redirect.github.com/spring-projects/spring-framework/issues/37035">#37035</a></li>
</ul>
<h2>:star: New Features</h2>
<ul>
<li>Ignore an empty port value in URI parsing <a href="https://redirect.github.com/spring-projects/spring-framework/issues/37117">#37117</a></li>
<li>Avoid retaining class files in annotation metadata <a href="https://redirect.github.com/spring-projects/spring-framework/pull/37112">#37112</a></li>
<li>Add <code>@Nullable</code> annotations when treating <code>Map.remove()</code> as returning <code>@Nullable</code> <a href="https://redirect.github.com/spring-projects/spring-framework/pull/37067">#37067</a></li>
<li>Revisit SSE view fragments handling <a href="https://redirect.github.com/spring-projects/spring-framework/issues/37061">#37061</a></li>
<li>Check list index after auto-grow in <code>AbstractNestablePropertyAccessor</code> <a href="https://redirect.github.com/spring-projects/spring-framework/issues/37036">#37036</a></li>
<li>Disable SpEL expression compilation by default in <code>SimpleEvaluationContext</code> <a href="https://redirect.github.com/spring-projects/spring-framework/issues/37035">#37035</a></li>
<li>Limit result size of <code>BigDecimal</code>/<code>BigInteger</code> power operations in SpEL <a href="https://redirect.github.com/spring-projects/spring-framework/issues/37034">#37034</a></li>
<li>Refactor redirect handling in UrlHandlerFilter <a href="https://redirect.github.com/spring-projects/spring-framework/issues/37030">#37030</a></li>
<li>Revise stylesheet source handling in XsltView <a href="https://redirect.github.com/spring-projects/spring-framework/issues/37029">#37029</a></li>
<li>Revise view name handling in UrlFilenameViewController <a href="https://redirect.github.com/spring-projects/spring-framework/issues/37027">#37027</a></li>
<li>Handle pre-flight requests in functional endpoint setup without DispatcherHandler <a href="https://redirect.github.com/spring-projects/spring-framework/issues/37024">#37024</a></li>
<li>Improve WebSocket handshake error logging <a href="https://redirect.github.com/spring-projects/spring-framework/issues/37023">#37023</a></li>
<li>Fix missing nullability in JdbcTemplate.batchUpdate <a href="https://redirect.github.com/spring-projects/spring-framework/pull/37012">#37012</a></li>
<li>Timeout property in RetryPolicy does not have a default constant <a href="https://redirect.github.com/spring-projects/spring-framework/issues/36983">#36983</a></li>
<li>Write native configuration files as UTF-8 <a href="https://redirect.github.com/spring-projects/spring-framework/pull/36972">#36972</a></li>
<li>DefaultServerRequest.ServletParametersMap.entrySet() does not retain HttpServletRequest.getParameterMap() order <a href="https://redirect.github.com/spring-projects/spring-framework/issues/36966">#36966</a></li>
<li>Perform nextKey within synchronization for SQLite as well <a href="https://redirect.github.com/spring-projects/spring-framework/issues/36959">#36959</a></li>
<li>Add support for custom ObjectInputFilter on DefaultDeserializer <a href="https://redirect.github.com/spring-projects/spring-framework/issues/36958">#36958</a></li>
<li>Revise resource bundle caching for common locales <a href="https://redirect.github.com/spring-projects/spring-framework/issues/36957">#36957</a></li>
<li>Improve nullability for <code>getSession(*)</code> in <code>MockHttpServletRequest</code> <a href="https://redirect.github.com/spring-projects/spring-framework/issues/36926">#36926</a></li>
<li>Improve fallback logic in ParameterContentNegotiationStrategy and ParameterContentTypeResolver <a href="https://redirect.github.com/spring-projects/spring-framework/issues/36925">#36925</a></li>
<li>Improve ambiguous match check on preflight request <a href="https://redirect.github.com/spring-projects/spring-framework/issues/36903">#36903</a></li>
<li>Improve Groovy markup template loading <a href="https://redirect.github.com/spring-projects/spring-framework/issues/36902">#36902</a></li>
<li>Improve request path handling on a Reactor Netty server <a href="https://redirect.github.com/spring-projects/spring-framework/issues/36893">#36893</a></li>
<li>Improve JettyWebSocketSession error handling <a href="https://redirect.github.com/spring-projects/spring-framework/issues/36891">#36891</a></li>
</ul>
<h2>:lady_beetle: Bug Fixes</h2>
<ul>
<li>EclipseLinkJpaDialect singleton lock in EclipseLinkConnectionHandle.getConnection() serializes all JDBC connection acquisitions under load <a href="https://redirect.github.com/spring-projects/spring-framework/issues/37085">#37085</a></li>
<li>MetadataReader fails to read byte[] array from annotation <a href="https://redirect.github.com/spring-projects/spring-framework/issues/37083">#37083</a></li>
<li>Ensure parsing/tostring symmetry in ContentDisposition <a href="https://redirect.github.com/spring-projects/spring-framework/issues/37064">#37064</a></li>
<li>Character outside of permitted range in Content Disposition <a href="https://redirect.github.com/spring-projects/spring-framework/issues/37062">#37062</a></li>
<li>Release Jackson BufferRecycler to its pool in encoders <a href="https://redirect.github.com/spring-projects/spring-framework/pull/37059">#37059</a></li>
<li>Ensure consistent error escaping <a href="https://redirect.github.com/spring-projects/spring-framework/issues/37055">#37055</a></li>
<li>Refine template name processing <a href="https://redirect.github.com/spring-projects/spring-framework/issues/37054">#37054</a></li>
<li>Reset TwoByteMatcher partial match on mismatching byte <a href="https://redirect.github.com/spring-projects/spring-framework/pull/37053">#37053</a></li>
<li>Refactor async XML parsing limit checks <a href="https://redirect.github.com/spring-projects/spring-framework/issues/37031">#37031</a></li>
<li>Fix part constraint checks in PartEventHttpMessageReader <a href="https://redirect.github.com/spring-projects/spring-framework/issues/37028">#37028</a></li>
<li>Fix buffer leak in RSocket SETUP frame handling <a href="https://redirect.github.com/spring-projects/spring-framework/issues/37026">#37026</a></li>
<li>Ensure correct Jetty core response cookie handling <a href="https://redirect.github.com/spring-projects/spring-framework/issues/37025">#37025</a></li>
<li>Align <code>domainToAscii</code> with current WhatWG spec <a href="https://redirect.github.com/spring-projects/spring-framework/issues/37018">#37018</a></li>
<li>Ensure consistent <code>ButtonTag</code> value attribute processing <a href="https://redirect.github.com/spring-projects/spring-framework/issues/37017">#37017</a></li>
</ul>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/spring-projects/spring-framework/commit/82a6b40b9366ec181ededdad282b307ee5381a52"><code>82a6b40</code></a> Release Spring Framework 7.0.9</li>
<li><a href="https://github.com/spring-projects/spring-framework/commit/a7b1b59cbd79175ed79b0f4aa967be70288bb2ee"><code>a7b1b59</code></a> Upgrade to Reactor 2025.0.7</li>
<li><a href="https://github.com/spring-projects/spring-framework/commit/996e3d3f18dbb13d92a2eaf988abfe77b06928d4"><code>996e3d3</code></a> Upgrade to Micrometer 1.16.7</li>
<li><a href="https://github.com/spring-projects/spring-framework/commit/73f5ddddcd4278580c9c879b2effdac29aa3bff5"><code>73f5ddd</code></a> Refactor maxInMemory limit handling for async XML parsing</li>
<li><a href="https://github.com/spring-projects/spring-framework/commit/675f25de72e7ac31db1ffe0dc324381d7cc1a308"><code>675f25d</code></a> Leading slash handling in UrlHandlerFilter</li>
<li><a href="https://github.com/spring-projects/spring-framework/commit/692dbc9160de3b5de50ccedeee014716d0cedb7b"><code>692dbc9</code></a> Apply ResourceHandlerUtils checks in XsltView</li>
<li><a href="https://github.com/spring-projects/spring-framework/commit/8647e90bc7b2da9b299566296f2b253a6fd9c128"><code>8647e90</code></a> Consistent maxPartSize check in PartEventHttpMessageReader</li>
<li><a href="https://github.com/spring-projects/spring-framework/commit/b9379e33d52b491e227715776a072d07bcccddab"><code>b9379e3</code></a> Check viewName for special prefixes in UrlFilenameViewController</li>
<li><a href="https://github.com/spring-projects/spring-framework/commit/a784dbe286beaa70e5a88638b6af7f58f458c07e"><code>a784dbe</code></a> Ensure Payload release on early error in createHeaders</li>
<li><a href="https://github.com/spring-projects/spring-framework/commit/3b492f3908df3fe0adc639b7da98f737d1ff7a02"><code>3b492f3</code></a> Return sameSite cookie value in Jetty response</li>
<li>Additional commits viewable in <a href="https://github.com/spring-projects/spring-framework/compare/v7.0.3...v7.0.9">compare view</a></li>
</ul>
</details>
<br />

Updates `org.springframework:spring-web` from 7.0.3 to 7.0.9
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/spring-projects/spring-framework/releases">org.springframework:spring-web's releases</a>.</em></p>
<blockquote>
<h2>v7.0.9</h2>
<h2>:warning: Attention Required</h2>
<ul>
<li>In Spring Framework 7.0.9, <code>ForwardedHeaderFilter</code> (Spring MVC) and <code>ForwardedHeaderTransformer</code> (WebFlux) each provide a boolean constructor argument whether to use the standard &quot;Forwarded&quot; header or the &quot;X-Forwarded&quot; alternative headers. A separate property turns on and off use of &quot;X-Forwarded-Prefix&quot;. While the default constructor preserves the existing behavior, we recommend to use the new constructor to explicitly specify which forwarded headers to use to make the processing more deterministic and aligned with what is expected from the proxy. Please, see the updated <a href="https://docs.spring.io/spring-framework/reference/7.0.9-SNAPSHOT/web/webmvc/filters.html#filters-forwarded-headers-security">Security Considerations</a> section for details. In 7.1 with <a href="https://redirect.github.com/spring-projects/spring-framework/issues/37072">#37072</a> the default constructor is deprecated and marked for removal. <a href="https://redirect.github.com/spring-projects/spring-framework/issues/37090">#37090</a></li>
<li>In Spring Framework 7.0.9, <code>SimpleEvaluationContext</code> no longer supports expression compilation by default, regardless of the compiler mode configured via <code>SpelParserConfiguration</code> or the <code>spring.expression.compiler.mode</code> system property or Spring property. Applications that intentionally use <code>SimpleEvaluationContext</code> with trusted expressions and require compilation for performance reasons can opt in by calling <code>withCompilationSupported()</code> on the <code>SimpleEvaluationContext</code> builder. Care should be taken whe...

_Description has been truncated_

<details><summary>Comment — nathanpond, 2026-08-31</summary>

**Not verified here** (2026-08-31): this group fixes the repo's only critical advisory (Spring Boot, archived-34) and the jackson-databind highs (archived-35), but it moves Spring Boot 4.0.2 → 4.1.1 (a minor with possible autoconfig changes) and the machine used for this review has no JDK/Maven, so `mvn test` could not be run. Recommended: run `mvn -q -f flowable-extension/pom.xml test` and rebuild the custom Flowable image (`infra/`) before merging — it should go in first among the Maven PRs.

</details>

---

## archived-97 — Bump org.junit.jupiter:junit-jupiter from 5.13.4 to 6.1.3 in /flowable-extension

`MERGED (merged 2026-08-31)` · app/dependabot · opened 2026-08-31 · `dependabot/maven/flowable-extension/org.junit.jupiter-junit-jupiter-6.1.3` → `master`

Bumps [org.junit.jupiter:junit-jupiter](https://github.com/junit-team/junit-framework) from 5.13.4 to 6.1.3.
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/junit-team/junit-framework/releases">org.junit.jupiter:junit-jupiter's releases</a>.</em></p>
<blockquote>
<p>JUnit 6.1.3 = Platform 6.1.3 + Jupiter 6.1.3 + Vintage 6.1.3</p>
<p>See <a href="https://docs.junit.org/6.1.3/release-notes.html">Release Notes</a>.</p>
<p><strong>Full Changelog</strong>: <a href="https://github.com/junit-team/junit-framework/compare/r6.1.2...r6.1.3">https://github.com/junit-team/junit-framework/compare/r6.1.2...r6.1.3</a></p>
<p>JUnit 6.1.2 = Platform 6.1.2 + Jupiter 6.1.2 + Vintage 6.1.2</p>
<p>See <a href="https://docs.junit.org/6.1.2/release-notes.html">Release Notes</a>.</p>
<p><strong>Full Changelog</strong>: <a href="https://github.com/junit-team/junit-framework/compare/r6.1.1...r6.1.2">https://github.com/junit-team/junit-framework/compare/r6.1.1...r6.1.2</a></p>
<p>JUnit 6.1.1 = Platform 6.1.1 + Jupiter 6.1.1 + Vintage 6.1.1</p>
<p>See <a href="https://docs.junit.org/6.1.1/release-notes.html">Release Notes</a>.</p>
<p><strong>Full Changelog</strong>: <a href="https://github.com/junit-team/junit-framework/compare/r6.1.0...r6.1.1">https://github.com/junit-team/junit-framework/compare/r6.1.0...r6.1.1</a></p>
<p>JUnit 6.1.0 = Platform 6.1.0 + Jupiter 6.1.0 + Vintage 6.1.0</p>
<p>See <a href="https://docs.junit.org/6.1.0/release-notes.html">Release Notes</a>.</p>
<h2>New Contributors</h2>
<ul>
<li><a href="https://github.com/JarvisCraft"><code>@​JarvisCraft</code></a> made their first contribution in <a href="https://redirect.github.com/junit-team/junit-framework/pull/5633">junit-team/junit-framework#5633</a></li>
<li><a href="https://github.com/Maran23"><code>@​Maran23</code></a> made their first contribution in <a href="https://redirect.github.com/junit-team/junit-framework/pull/5644">junit-team/junit-framework#5644</a></li>
</ul>
<p><strong>Full Changelog</strong>: <a href="https://github.com/junit-team/junit-framework/compare/r6.0.3...r6.1.0">https://github.com/junit-team/junit-framework/compare/r6.0.3...r6.1.0</a></p>
<p>JUnit 6.1.0-RC1 = Platform 6.1.0-RC1 + Jupiter 6.1.0-RC1 + Vintage 6.1.0-RC1</p>
<p>See <a href="https://docs.junit.org/6.1.0-RC1/release-notes/">Release Notes</a>.</p>
<h2>New Contributors</h2>
<ul>
<li><a href="https://github.com/mariokhoury4"><code>@​mariokhoury4</code></a> made their first contribution in <a href="https://redirect.github.com/junit-team/junit-framework/pull/4574">junit-team/junit-framework#4574</a></li>
<li><a href="https://github.com/Ogu1208"><code>@​Ogu1208</code></a> made their first contribution in <a href="https://redirect.github.com/junit-team/junit-framework/pull/5145">junit-team/junit-framework#5145</a></li>
<li><a href="https://github.com/HyungGeun94"><code>@​HyungGeun94</code></a> made their first contribution in <a href="https://redirect.github.com/junit-team/junit-framework/pull/5271">junit-team/junit-framework#5271</a></li>
<li><a href="https://github.com/yalishevant"><code>@​yalishevant</code></a> made their first contribution in <a href="https://redirect.github.com/junit-team/junit-framework/pull/5316">junit-team/junit-framework#5316</a></li>
<li><a href="https://github.com/JINU-CHANG"><code>@​JINU-CHANG</code></a> made their first contribution in <a href="https://redirect.github.com/junit-team/junit-framework/pull/5290">junit-team/junit-framework#5290</a></li>
<li><a href="https://github.com/jaschdoc"><code>@​jaschdoc</code></a> made their first contribution in <a href="https://redirect.github.com/junit-team/junit-framework/pull/5427">junit-team/junit-framework#5427</a></li>
<li><a href="https://github.com/kawshikbuet17"><code>@​kawshikbuet17</code></a> made their first contribution in <a href="https://redirect.github.com/junit-team/junit-framework/pull/5561">junit-team/junit-framework#5561</a></li>
<li><a href="https://github.com/msridhar"><code>@​msridhar</code></a> made their first contribution in <a href="https://redirect.github.com/junit-team/junit-framework/pull/5602">junit-team/junit-framework#5602</a></li>
</ul>
<p><strong>Full Changelog</strong>: <a href="https://github.com/junit-team/junit-framework/compare/r6.1.0-M1...r6.1.0-RC1">https://github.com/junit-team/junit-framework/compare/r6.1.0-M1...r6.1.0-RC1</a></p>
<p>JUnit 6.1.0-M1 = Platform 6.1.0-M1 + Jupiter 6.1.0-M1 + Vintage 6.1.0-M1</p>
<p>See <a href="https://docs.junit.org/6.1.0-M1/release-notes/">Release Notes</a>.</p>
<h2>New Contributors</h2>
<ul>
<li><a href="https://github.com/vy"><code>@​vy</code></a> made their first contribution in <a href="https://redirect.github.com/junit-team/junit-framework/pull/5041">junit-team/junit-framework#5041</a></li>
</ul>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/junit-team/junit-framework/commit/f59f60d2cebdf2224235d81f781b1f310cbc8138"><code>f59f60d</code></a> Release 6.1.3</li>
<li><a href="https://github.com/junit-team/junit-framework/commit/cd8ec9202ce8260a434edb38a8565e36f620e8d6"><code>cd8ec92</code></a> Finalize 6.1.3 release notes</li>
<li><a href="https://github.com/junit-team/junit-framework/commit/c8729f26aacd8e2dd6fa564b929fe047faaa7dc9"><code>c8729f2</code></a> Restore compatibility with GraalVM 25 (<a href="https://redirect.github.com/junit-team/junit-framework/issues/5901">#5901</a>)</li>
<li><a href="https://github.com/junit-team/junit-framework/commit/ddc9e74e5d21c2dd18bc0cfe4681d5ff50ac379c"><code>ddc9e74</code></a> Update graalvm/setup-graalvm action to v1.6.4 (<a href="https://redirect.github.com/junit-team/junit-framework/issues/5959">#5959</a>)</li>
<li><a href="https://github.com/junit-team/junit-framework/commit/fe2c52a780fcbda6c7ccf5d6576beb7ad2d80bf8"><code>fe2c52a</code></a> Update plugin org.graalvm.buildtools.native to v1.1.7 (<a href="https://redirect.github.com/junit-team/junit-framework/issues/5923">#5923</a>)</li>
<li><a href="https://github.com/junit-team/junit-framework/commit/62afc02b541ee9dfa16b121705f79b7d44f4dae0"><code>62afc02</code></a> Delay GraalVM plugin updates for 3 days</li>
<li><a href="https://github.com/junit-team/junit-framework/commit/0cc29022801365a409c1213a811a8877cb88cce3"><code>0cc2902</code></a> Skip <code>graalVmTest</code> task if GraalVM env vars are not set</li>
<li><a href="https://github.com/junit-team/junit-framework/commit/f6bbfc53e7e00b1b25e7d3b0bc09eef05cc4b721"><code>f6bbfc5</code></a> Move GraalVM tests to separate test task (<a href="https://redirect.github.com/junit-team/junit-framework/issues/5903">#5903</a>)</li>
<li><a href="https://github.com/junit-team/junit-framework/commit/e87e0521d4fbd75116a5fda42566fcb7a56eb340"><code>e87e052</code></a> Update plugin org.graalvm.buildtools.native to v1.1.6 (<a href="https://redirect.github.com/junit-team/junit-framework/issues/5899">#5899</a>)</li>
<li><a href="https://github.com/junit-team/junit-framework/commit/1cd56df89754fb8523ac9fbbf888e9a552e33ee8"><code>1cd56df</code></a> Update plugin org.graalvm.buildtools.native to v1.1.5 (<a href="https://redirect.github.com/junit-team/junit-framework/issues/5880">#5880</a>)</li>
<li>Additional commits viewable in <a href="https://github.com/junit-team/junit-framework/compare/r5.13.4...r6.1.3">compare view</a></li>
</ul>
</details>
<br />

<details><summary>Comment — nathanpond, 2026-08-31</summary>

**Held** (2026-08-31): JUnit 5 → 6 is a major (new namespaces/API removals); not verifiable here without a JDK. Take after archived-96.

</details>

---

## archived-98 — Bump bpmn-js-create-append-anything from 1.2.0 to 2.0.0

`MERGED (merged 2026-08-31)` · app/dependabot · opened 2026-08-31 · `dependabot/npm_and_yarn/bpmn-js-create-append-anything-2.0.0` → `master`

Bumps [bpmn-js-create-append-anything](https://github.com/bpmn-io/bpmn-js-create-append-anything) from 1.2.0 to 2.0.0.
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/bpmn-io/bpmn-js-create-append-anything/releases">bpmn-js-create-append-anything's releases</a>.</em></p>
<blockquote>
<h2>v2.0.0</h2>
<ul>
<li>chore: update actions/setup-node action to v7 <a href="https://github.com/bpmn-io/bpmn-js-create-append-anything/commit/6aff4353b10a6c3a6a8cb5080a911189632bb021">https://github.com/bpmn-io/bpmn-js-create-append-anything/commit/6aff4353b10a6c3a6a8cb5080a911189632bb021</a></li>
<li>feat: reuse shared menu entries list and allow to style menus width with css variable <a href="https://github.com/bpmn-io/bpmn-js-create-append-anything/commit/bf8607b4d5533a9a7b94a6374568b1db035541a0">https://github.com/bpmn-io/bpmn-js-create-append-anything/commit/bf8607b4d5533a9a7b94a6374568b1db035541a0</a></li>
</ul>
<hr />
<p><a href="https://github.com/bpmn-io/bpmn-js-create-append-anything/compare/v1.3.1...v2.0.0">https://github.com/bpmn-io/bpmn-js-create-append-anything/compare/v1.3.1...v2.0.0</a></p>
<h2>v1.3.1</h2>
<ul>
<li><code>FIX</code>: disallow append action for compensation activities (<a href="https://redirect.github.com/bpmn-io/bpmn-js-create-append-anything/issues/86">#86</a>)</li>
</ul>
<h2>v1.3.0</h2>
<ul>
<li>chore(CHANGELOG): update to v1.3.0  769596e</li>
<li>chore: require diagram-js &gt;= 15.18  c7ff80c</li>
<li>test: add helper to get nested entries  884a629</li>
<li>chore: simplify example connector  b09d36f</li>
<li>chore: extract entry base  2ae39c4</li>
<li>test: simplify search test  8a8ade6</li>
<li>chore: remove duplicated icon  08118a8</li>
<li>feat: support multi-step pop-up for templates  c95c60c</li>
<li>chore: bump modeling deps  c291502</li>
<li>chore(refactor): unify template entry building  092e2c9</li>
<li>chore: switch to <code>karma-tldr-reporter</code>  cbafd3d</li>
<li>chore: switch to <code>karma-tldr-reporter</code>  bd542c6</li>
<li>ci: remove puppeteer  f96eb42</li>
<li>chore: update codecov/codecov-action action to v7  cf29d37</li>
<li>chore: update dependency npm-run-all2 to v9  269ab5b</li>
<li>chore: move executablePath inside karma function and make async  5cfd4dd</li>
<li>chore: update dependency puppeteer to v25  8791c64</li>
<li>chore: update dependency sinon to v22  c87afaf</li>
<li>chore: update babel*  38e016c</li>
<li>chore: update to diagram-js@15.12.0  ea0fe5f</li>
<li>chore: update rollup*  f6e03a1</li>
<li>chore: update puppeteer  3b93515</li>
<li>chore: update to sinon@21.1.2  f45ffe4</li>
<li>chore: update eslint*  ee4118c</li>
<li>chore: update webpack*  22256f3</li>
<li>chore: update to bpmn-js-element-templates@2.23.1  3e360e6</li>
<li>chore: update dependency babel-plugin-istanbul to v8  e03273f</li>
<li>chore: update to bpmn-js-element-templates@2.23.0  58e4d6b</li>
<li>chore: update to bpmn-js@18.14.0  d760a98</li>
<li>chore: update codecov/codecov-action action to v6  9b46517</li>
<li>chore: update bpmn-io dev dependencies  67cf8ea</li>
<li>chore: update changelog  8e870e3</li>
</ul>
<hr />
<p><a href="https://github.com/bpmn-io/bpmn-js-create-append-anything/compare/v1.2.0...v1.3.0">https://github.com/bpmn-io/bpmn-js-create-append-anything/compare/v1.2.0...v1.3.0</a></p>
</blockquote>
</details>
<details>
<summary>Changelog</summary>
<p><em>Sourced from <a href="https://github.com/bpmn-io/bpmn-js-create-append-anything/blob/main/CHANGELOG.md">bpmn-js-create-append-anything's changelog</a>.</em></p>
<blockquote>
<h2>2.0.0</h2>
<ul>
<li><code>FEAT</code>: allow overriding the create/append menu width via css variable (<a href="https://redirect.github.com/bpmn-io/bpmn-js-create-append-anything/pull/88">#88</a>)</li>
<li><code>FEAT</code>: reuse shared menu entries list from <code>bpmn-js</code> (<a href="https://redirect.github.com/bpmn-io/bpmn-js-create-append-anything/pull/88">#88</a>)</li>
</ul>
<h3>Breaking Changes</h3>
<ul>
<li>Require <code>bpmn-js@18.22.0</code> as a peer dependency (<a href="https://redirect.github.com/bpmn-io/bpmn-js-create-append-anything/pull/88">#88</a>)</li>
</ul>
<h2>1.3.1</h2>
<ul>
<li><code>FIX</code>: disallow append action for compensation activities (<a href="https://redirect.github.com/bpmn-io/bpmn-js-create-append-anything/issues/86">#86</a>)</li>
</ul>
<h2>1.3.0</h2>
<ul>
<li><code>FEAT</code>: support multi-step pop-up for templates (<a href="https://redirect.github.com/bpmn-io/bpmn-js-create-append-anything/issues/78">#78</a>)</li>
</ul>
</blockquote>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/bpmn-io/bpmn-js-create-append-anything/commit/eda6267a0084fa9061bf0d213ad46bfeeae820fc"><code>eda6267</code></a> 2.0.0</li>
<li><a href="https://github.com/bpmn-io/bpmn-js-create-append-anything/commit/462e705606927490562d536b4ecc2ec9f504579c"><code>462e705</code></a> chore: update CHANGELOG.md</li>
<li><a href="https://github.com/bpmn-io/bpmn-js-create-append-anything/commit/739b1c88bad2dfc98bb7d90bdab60f1fffaaea84"><code>739b1c8</code></a> chore: update CHANGELOG.md</li>
<li><a href="https://github.com/bpmn-io/bpmn-js-create-append-anything/commit/4732f6b52b372530610fbe94e03e66a963dc9fcd"><code>4732f6b</code></a> test: override menu width</li>
<li><a href="https://github.com/bpmn-io/bpmn-js-create-append-anything/commit/a0e8c4e7d9ee66012f538fcf630b991107dffdf6"><code>a0e8c4e</code></a> chore: adjust jsdoc</li>
<li><a href="https://github.com/bpmn-io/bpmn-js-create-append-anything/commit/8100891a4466beaf1764519a0ea513241ef1bdf8"><code>8100891</code></a> chore: add peer dependency on bpmn-js@18.22.0</li>
<li><a href="https://github.com/bpmn-io/bpmn-js-create-append-anything/commit/39fafe4d76bac10fafd9315082ad3191422a3905"><code>39fafe4</code></a> feat: allow overriding the create/append menu width via css variable</li>
<li><a href="https://github.com/bpmn-io/bpmn-js-create-append-anything/commit/339b487cc2d961a1ed54f8df2a1e79f06b2decb8"><code>339b487</code></a> deps: update bpmn-js to <code>18.22.0</code> and diagram-js peer deps to <code>15.23.0</code></li>
<li><a href="https://github.com/bpmn-io/bpmn-js-create-append-anything/commit/bf8607b4d5533a9a7b94a6374568b1db035541a0"><code>bf8607b</code></a> chore: reuse shared menu entries list from bpmn-js and expose it for further ...</li>
<li><a href="https://github.com/bpmn-io/bpmn-js-create-append-anything/commit/6aff4353b10a6c3a6a8cb5080a911189632bb021"><code>6aff435</code></a> chore: update actions/setup-node action to v7</li>
<li>Additional commits viewable in <a href="https://github.com/bpmn-io/bpmn-js-create-append-anything/compare/v1.2.0...v2.0.0">compare view</a></li>
</ul>
</details>
<br />

<details><summary>Comment — nathanpond, 2026-08-31</summary>

@dependabot rebase

</details>

---

## archived-99 — chore(deps-dev): bump typescript from 5.9.3 to 7.0.2 in /services/executor

`MERGED (merged 2026-08-31)` · app/dependabot · opened 2026-08-31 · `dependabot/npm_and_yarn/services/executor/typescript-7.0.2` → `master`

Bumps [typescript](https://github.com/microsoft/TypeScript) from 5.9.3 to 7.0.2.
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/microsoft/TypeScript/releases">typescript's releases</a>.</em></p>
<blockquote>
<h2>TypeScript 7.0.2</h2>
<p><a href="https://devblogs.microsoft.com/typescript/announcing-typescript-7-0/">https://devblogs.microsoft.com/typescript/announcing-typescript-7-0/</a></p>
<p>This tag was originally released at: <a href="https://github.com/microsoft/typescript-go/releases/tag/typescript%2Fv7.0.2">https://github.com/microsoft/typescript-go/releases/tag/typescript%2Fv7.0.2</a></p>
<h2>TypeScript 6.0.3</h2>
<p>For release notes, check out the <a href="https://devblogs.microsoft.com/typescript/announcing-typescript-6-0/">release announcement blog post</a>.</p>
<ul>
<li><a href="https://github.com/Microsoft/TypeScript/issues?utf8=%E2%9C%93&amp;q=milestone%3A%22TypeScript+6.0.0%22">fixed issues query for TypeScript 6.0.0 (Beta)</a>.</li>
<li><a href="https://github.com/Microsoft/TypeScript/issues?utf8=%E2%9C%93&amp;q=milestone%3A%22TypeScript+6.0.1%22">fixed issues query for TypeScript 6.0.1 (RC)</a>.</li>
<li><a href="https://github.com/Microsoft/TypeScript/issues?utf8=%E2%9C%93&amp;q=milestone%3A%22TypeScript+6.0.2%22">fixed issues query for TypeScript 6.0.2 (Stable)</a>.</li>
<li><a href="https://github.com/Microsoft/TypeScript/issues?utf8=%E2%9C%93&amp;q=milestone%3A%22TypeScript+6.0.3%22">fixed issues query for TypeScript 6.0.3 (Stable)</a>.</li>
</ul>
<p>Downloads are available on:</p>
<ul>
<li><a href="https://www.npmjs.com/package/typescript">npm</a></li>
</ul>
<h2>TypeScript 6.0</h2>
<p>For release notes, check out the <a href="https://devblogs.microsoft.com/typescript/announcing-typescript-6-0/">release announcement blog post</a>.</p>
<ul>
<li><a href="https://github.com/Microsoft/TypeScript/issues?utf8=%E2%9C%93&amp;q=milestone%3A%22TypeScript+6.0.0%22">fixed issues query for TypeScript 6.0.0 (Beta)</a>.</li>
<li><a href="https://github.com/Microsoft/TypeScript/issues?utf8=%E2%9C%93&amp;q=milestone%3A%22TypeScript+6.0.1%22">fixed issues query for TypeScript 6.0.1 (RC)</a>.</li>
<li><a href="https://github.com/Microsoft/TypeScript/issues?utf8=%E2%9C%93&amp;q=milestone%3A%22TypeScript+6.0.2%22">fixed issues query for TypeScript 6.0.2 (Stable)</a>.</li>
</ul>
<p>Downloads are available on:</p>
<ul>
<li><a href="https://www.npmjs.com/package/typescript">npm</a></li>
</ul>
<h2>TypeScript 6.0.1 RC</h2>
<p>For release notes, check out the <a href="https://devblogs.microsoft.com/typescript/announcing-typescript-6-0-rc/">release announcement blog post</a>.</p>
<ul>
<li><a href="https://github.com/Microsoft/TypeScript/issues?utf8=%E2%9C%93&amp;q=milestone%3A%22TypeScript+6.0.0%22">fixed issues query for TypeScript 6.0.0 (Beta)</a>.</li>
<li><a href="https://github.com/Microsoft/TypeScript/issues?utf8=%E2%9C%93&amp;q=milestone%3A%22TypeScript+6.0.1%22">fixed issues query for TypeScript 6.0.1 (RC)</a>.</li>
</ul>
<p>Downloads are available on:</p>
<ul>
<li><a href="https://www.npmjs.com/package/typescript">npm</a></li>
</ul>
<h2>TypeScript 6.0 Beta</h2>
<p>For release notes, check out the <a href="https://devblogs.microsoft.com/typescript/announcing-typescript-6-0-beta/">release announcement</a>.</p>
<ul>
<li><a href="https://github.com/Microsoft/TypeScript/issues?utf8=%E2%9C%93&amp;q=milestone%3A%22TypeScript+6.0.0%22+is%3Aclosed+">fixed issues query for Typescript 6.0.0 (Beta)</a>.</li>
</ul>
<p>Downloads are available on:</p>
<ul>
<li><a href="https://www.npmjs.com/package/typescript">npm</a></li>
</ul>
</blockquote>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/microsoft/TypeScript/commit/1e4744d68260a7cb91b62b12edc3f6a2187faaf1"><code>1e4744d</code></a> Merge branch 'main' into ts7-release</li>
<li><a href="https://github.com/microsoft/TypeScript/commit/a5a219c3b5da0db4fa0ecf6c0b1f588c9af9c669"><code>a5a219c</code></a><code>microsoft/typescript-go#4558</code></li>
<li><a href="https://github.com/microsoft/TypeScript/commit/ecfe30dce91368d52c9a49b6095bb0b673a238f8"><code>ecfe30d</code></a> Update status localization</li>
<li><a href="https://github.com/microsoft/TypeScript/commit/5de25b5f8fec2ca35eadaed041f1f06d2e214895"><code>5de25b5</code></a> Hide executable name in TypeScript status</li>
<li><a href="https://github.com/microsoft/TypeScript/commit/d7ce74a75da2b80e8201506a1599c06549432b93"><code>d7ce74a</code></a> Show bundled TypeScript version for packaged servers</li>
<li><a href="https://github.com/microsoft/TypeScript/commit/29be66a607707f90d7a53103a4469bb3015a4d54"><code>29be66a</code></a> Correct TS 7 release version to 7.0.2</li>
<li><a href="https://github.com/microsoft/TypeScript/commit/ed2bd1bfa4aac5211ce4bc58fcd1313c7eddc8ff"><code>ed2bd1b</code></a> Merge branch 'main' into ts7-release</li>
<li><a href="https://github.com/microsoft/TypeScript/commit/887307575c58ea640dbeba3b4e8fdb6347cd3044"><code>8873075</code></a> Bump the github-actions group across 1 directory with 3 updates (microsoft/ty...</li>
<li><a href="https://github.com/microsoft/TypeScript/commit/9427131ae2d4e230a90ee8a09daac4e75da3e311"><code>9427131</code></a> Set up stable / nightly extension split, other prep (microsoft/typescript-go#...</li>
<li><a href="https://github.com/microsoft/TypeScript/commit/d4eaca5460a1f5f02a829e62706794b0a6fb903e"><code>d4eaca5</code></a><code>microsoft/typescript-go#4549</code></li>
<li>Additional commits viewable in <a href="https://github.com/microsoft/TypeScript/compare/v5.9.3...v7.0.2">compare view</a></li>
</ul>
</details>
<details>
<summary>Maintainer changes</summary>
<p>This version was pushed to npm by <a href="https://www.npmjs.com/~microsoft1es">microsoft1es</a>, a new releaser for typescript since your current version.</p>
</details>
<br />

<details><summary>Comment — nathanpond, 2026-08-31</summary>

**Held** (2026-08-31): TypeScript 7 is the new native (Go) compiler — a major toolchain change. Not adopting it via a Dependabot bump; needs a deliberate upgrade of `typescript-eslint`, Vite plugin and build scripts together, verified across the SPA and both sidecars.

</details>

<details><summary>Comment — nathanpond, 2026-08-31</summary>

@dependabot rebase

</details>

<details><summary>Comment — nathanpond, 2026-08-31</summary>

Validated against current `master` (which now carries the TS 7 tsconfig groundwork from archived-170) in an isolated worktree:

- `typescript@7.0.2` installed, `npx tsc --version` → 7.0.2
- `npm test` (build + `node --test`): **11 passed / 0 failed** — including the sandbox suite (timeout, memory cap, isolation, host-escape probes)

The executor has no ESLint, no bundler and no Vite plugin — its whole toolchain is `tsc` — so the "upgrade typescript-eslint and the Vite plugin together" hold I put on this PR applied to archived-109 (the SPA), not here. Unblocking it on its own merits.

For the record, the SPA hold stands and is a hard external blocker: `typescript-eslint@8.69.0` declares `typescript: ">=4.8.4 <6.1.0"`, so TS 7 is out of range until upstream ships support.

</details>

---

## archived-100 — Bump org.graalvm.js:js-scriptengine from 24.1.2 to 25.3.4.1 in /flowable-extension

`MERGED (merged 2026-08-31)` · app/dependabot · opened 2026-08-31 · `dependabot/maven/flowable-extension/org.graalvm.js-js-scriptengine-25.3.4.1` → `master`

Bumps [org.graalvm.js:js-scriptengine](https://github.com/oracle/graaljs) from 24.1.2 to 25.3.4.1.
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/oracle/graaljs/releases">org.graalvm.js:js-scriptengine's releases</a>.</em></p>
<blockquote>
<h2>GraalJS 25 Innovation 3 (25.3.4.1)</h2>
<p><a href="https://www.graalvm.org/javascript/">GraalJS</a> is an ECMAScript-compliant runtime to execute JavaScript and Node.js applications.
It is fully standard-compliant, executes applications with high performance, and provides all benefits from the GraalVM stack, including language interoperability and common tooling.</p>
<p>You can download GraalJS as a Native Standalone distribution for Oracle GraalVM or GraalVM Community Edition.
Native Standalone contains a Native Image compiled launcher.</p>
<p>Starting with 25.0.3, JVM Standalone artifacts are no longer supported and are not included in this release.</p>
<p>To distinguish between Oracle GraalVM and GraalVM Community Edition, the Community Edition distribution has the suffix <code>-community</code> in its name.</p>
<p>Learn more about GraalJS and how to get started on the website at <a href="https://www.graalvm.org/javascript/">https://www.graalvm.org/javascript/</a>.</p>
<h2>GraalJS 25 Innovation 2 (25.2.4)</h2>
<p><a href="https://www.graalvm.org/javascript/">GraalJS</a> is an ECMAScript-compliant runtime to execute JavaScript and Node.js applications.
It is fully standard-compliant, executes applications with high performance, and provides all benefits from the GraalVM stack, including language interoperability and common tooling.</p>
<p>You can download GraalJS as a Native Standalone distribution for Oracle GraalVM or GraalVM Community Edition.
Native Standalone contains a Native Image compiled launcher.</p>
<p>Starting with 25.0.3, JVM Standalone artifacts are no longer supported and are not included in this release.</p>
<p>To distinguish between Oracle GraalVM and GraalVM Community Edition, the Community Edition distribution has the suffix <code>-community</code> in its name.</p>
<p>Learn more about GraalJS and how to get started on the website at <a href="https://www.graalvm.org/javascript/">https://www.graalvm.org/javascript/</a>.</p>
<h2>GraalJS 25 Innovation 1 (25.1.3)</h2>
<p><a href="https://www.graalvm.org/javascript/">GraalJS</a> is an ECMAScript-compliant runtime to execute JavaScript and Node.js applications.
It is fully standard-compliant, executes applications with high performance, and provides all benefits from the GraalVM stack, including language interoperability and common tooling.</p>
<p>You can download GraalJS as a Native Standalone distribution for Oracle GraalVM or GraalVM Community Edition.
Native Standalone contains a Native Image compiled launcher.</p>
<p>Starting with 25.0.3, JVM Standalone artifacts are no longer supported and are not included in this release.</p>
<p>To distinguish between Oracle GraalVM and GraalVM Community Edition, the Community Edition distribution has the suffix <code>-community</code> in its name.</p>
<p>Learn more about GraalJS and how to get started on the website at <a href="https://www.graalvm.org/javascript/">https://www.graalvm.org/javascript/</a>.</p>
<h2>GraalJS 25.0.3</h2>
<p><a href="https://www.graalvm.org/javascript/">GraalJS</a> is an ECMAScript-compliant runtime to execute JavaScript and Node.js applications.
It is fully standard-compliant, executes applications with high performance, and provides all benefits from the GraalVM stack, including language interoperability and common tooling.</p>
<p>You can download GraalJS as a Native Standalone distribution for Oracle GraalVM or GraalVM Community Edition.
Native Standalone contains a Native Image compiled launcher.</p>
<p>Starting with 25.0.3, JVM Standalone artifacts are no longer supported and are not included in this release.</p>
<p>To distinguish between Oracle GraalVM and GraalVM Community Edition, the Community Edition distribution has the suffix <code>-community</code> in its name.</p>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Changelog</summary>
<p><em>Sourced from <a href="https://github.com/oracle/graaljs/blob/master/CHANGELOG.md">org.graalvm.js:js-scriptengine's changelog</a>.</em></p>
<blockquote>
<h2>Version 25.3.4.1</h2>
<ul>
<li>Implemented the <a href="https://github.com/tc39/proposal-iterator-includes"><code>Iterator Includes</code></a> proposal. It is available in ECMAScript staging mode (<code>--js.ecmascript-version=staging</code>).</li>
<li>Implemented the <a href="https://github.com/tc39/proposal-iterator-join"><code>Iterator Join</code></a> proposal. It is available in ECMAScript staging mode (<code>--js.ecmascript-version=staging</code>).</li>
<li>Implemented the <a href="https://github.com/tc39/proposal-iterator-chunking"><code>Iterator Chunking</code></a> proposal. It is available in ECMAScript staging mode (<code>--js.ecmascript-version=staging</code>).</li>
<li>Updated Node.js to version 24.18.1.</li>
</ul>
<h2>Version 25.2.4</h2>
<ul>
<li>Updated Node.js to version 24.17.0.</li>
</ul>
<h2>Version 25.1.3</h2>
<ul>
<li>ECMAScript 2026 mode/features enabled by default.</li>
<li>Removed support and builds for macOS x86-64 (darwin-amd64).</li>
<li>Added an experimental option <code>js.crypto</code> that provides <code>getRandomValues()</code> and <code>randomUUID()</code> from the <a href="https://w3c.github.io/webcrypto/#crypto-interface">Web Crypto API</a>.</li>
<li>Added stable option <code>js.performance</code> that provides <code>performance.now()</code>, <code>timeOrigin</code>, and <code>toJSON()</code> from the <a href="https://w3c.github.io/hr-time/">Web High Resolution Time API</a>.</li>
<li>Finished support for <a href="https://github.com/tc39/proposal-temporal">Temporal</a>. It is available in ECMAScript 2027 mode (<code>--js.ecmascript-version=2027</code>).</li>
<li>Implemented the <a href="https://github.com/tc39/proposal-immutable-arraybuffer"><code>Immutable ArrayBuffers</code></a> proposal. It is available in ECMAScript staging mode (<code>--js.ecmascript-version=staging</code>).</li>
<li>Implemented the <a href="https://github.com/tc39/proposal-explicit-resource-management"><code>Explicit Resource Management</code></a> proposal. It is available behind the experimental option (<code>--js.explicit-resource-management</code>).</li>
<li>Updated Node.js to version 24.14.1.</li>
<li>Limited Chrome inspector remote debugging to localhost.</li>
<li>Implemented the <a href="https://github.com/tc39/proposal-joint-iteration"><code>Joint Iteration</code></a> proposal. It is available in ECMAScript staging mode (<code>--js.ecmascript-version=staging</code>).</li>
<li>Implemented the <a href="https://github.com/tc39/proposal-import-text"><code>Import Text</code></a> proposal. It is available behind the experimental option (<code>--js.import-text</code>).</li>
<li>Implemented the <a href="https://github.com/tc39/proposal-import-bytes"><code>Import Bytes</code></a> proposal. It is available behind the experimental option (<code>--js.import-bytes</code>).</li>
<li>Implemented the <a href="https://github.com/tc39/proposal-error-stack-accessor"><code>Error Stack Accessor</code></a> proposal. It is available behind the experimental option (<code>--js.error-stack-accessor</code>).</li>
<li>Removed support for legacy import assertions (<code>import ... assert {type: &quot;...&quot;}</code>) and the <code>--js.import-assertions</code> option; use import attributes (<code>import ... with {type: &quot;...&quot;}</code>, option <code>--js.import-attributes</code>) instead.</li>
</ul>
<h2>Version 25.0.0</h2>
<ul>
<li>ECMAScript 2025 mode/features enabled by default.</li>
<li>Updated Node.js to version 22.17.1.</li>
<li>Implemented the <a href="https://github.com/tc39/proposal-intl-duration-format"><code>Intl.DurationFormat</code></a> proposal.</li>
<li>Made option <code>js.text-encoding</code> stable and allowed in <code>SandboxPolicy.CONSTRAINED</code>.</li>
<li>Implemented the <a href="https://github.com/tc39/proposal-defer-import-eval"><code>import defer</code></a> proposal. It is available in ECMAScript staging mode (<code>--js.ecmascript-version=staging</code>).</li>
<li>Implemented the <a href="https://github.com/tc39/proposal-upsert"><code>Upsert</code></a> proposal. It is available in ECMAScript staging mode (<code>--js.ecmascript-version=staging</code>).</li>
<li>Enabled source phase imports from WebAssembly modules (<code>import source mod from &quot;./mod.wasm&quot;</code>) by default if the <code>js.webassembly</code> option is enabled and the <code>js.source-phase-imports</code> option is not explicitly set to <code>false</code>.</li>
</ul>
<h2>Version 24.2.0</h2>
<ul>
<li>Updated Node.js to version 22.13.1.</li>
<li>Implemented the <a href="https://github.com/tc39/proposal-is-error"><code>Error.isError</code></a> proposal. It is available in ECMAScript staging mode (<code>--js.ecmascript-version=staging</code>).</li>
<li>Implemented the <a href="https://github.com/tc39/proposal-math-sum"><code>Math.sumPrecise</code></a> proposal. It is available in ECMAScript staging mode (<code>--js.ecmascript-version=staging</code>).</li>
<li>Implemented the <a href="https://github.com/tc39/proposal-promise-try"><code>Promise.try</code></a> proposal. It is available in ECMAScript staging mode (<code>--js.ecmascript-version=staging</code>).</li>
<li>Implemented the <a href="https://github.com/tc39/proposal-atomics-microwait"><code>Atomics.pause</code></a> proposal. It is available in ECMAScript staging mode (<code>--js.ecmascript-version=staging</code>).</li>
<li>Implemented the <a href="https://github.com/tc39/proposal-arraybuffer-base64">Uint8Array to/from base64 and hex</a> proposal. It is available in ECMAScript staging mode (<code>--js.ecmascript-version=staging</code>).</li>
<li>Implemented the <a href="https://github.com/tc39/proposal-source-phase-imports">Source Phase Imports</a> proposal. It is available behind the experimental option (<code>--js.source-phase-imports</code>).</li>
<li>Implemented the <a href="https://github.com/WebAssembly/esm-integration">WebAssembly/ES Module Integration</a> proposal, allowing <code>.wasm</code> modules to be loaded via <code>import</code> statements.</li>
<li>Implemented basic Worker API (resembling the API available in <code>d8</code>). It is available behind the experimental option <code>--js.worker</code>.</li>
<li>Added option <code>js.stack-trace-api</code> that enables/disables <code>Error.captureStackTrace</code>, <code>Error.prepareStackTrace</code> and <code>Error.stackTraceLimit</code>. These non-standard extensions are disabled by default (unless <code>js.v8-compat</code> or <code>js.nashorn-compat</code> is used).</li>
<li>Made option <code>js.webassembly</code> stable.</li>
<li>Made options <code>js.load</code>, <code>js.print</code>, and <code>js.graal-builtin</code> stable and allowed in <code>SandboxPolicy.UNTRUSTED</code>.</li>
<li>Made option <code>js.locale</code> stable and allowed in <code>SandboxPolicy.UNTRUSTED</code>. Its value, if non-empty, must be a well-formed Unicode BCP 47 locale identifier and is now validated.</li>
<li>Added an experimental <code>java.util.concurrent.Executor</code> that can be used to post tasks into the event loop thread in <code>graal-nodejs</code>. It is available as <code>require('node:graal').eventLoopExecutor</code>.</li>
<li>Implemented the <code>TextDecoder</code> and <code>TextEncoder</code> APIs of the <a href="https://encoding.spec.whatwg.org/">WHATWG Encoding Standard</a>. They are available behind the experimental option (<code>--js.text-encoding</code>).</li>
</ul>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/oracle/graaljs/commit/aede65d92c3b099fe2c732d70c4885cee30bb0aa"><code>aede65d</code></a> [GR-78747] Fix iterator closing for <code>Array.from</code> and collection constructors.</li>
<li><a href="https://github.com/oracle/graaljs/commit/4d5b9b64d0df7b29726da709de1222aa709738e8"><code>4d5b9b6</code></a> [GR-78730] Avoid duplicate builtin key and name strings.</li>
<li><a href="https://github.com/oracle/graaljs/commit/5cfa11daf3748e7f112637130006bba4a0033d63"><code>5cfa11d</code></a> Update test262 status.</li>
<li><a href="https://github.com/oracle/graaljs/commit/0daf607f80c0a08444ddeec232fd5ee1cccc05c9"><code>0daf607</code></a> Fix iterator closing for <code>Array.from</code> and collection constructors.</li>
<li><a href="https://github.com/oracle/graaljs/commit/735f95c65bab103de089adf4c9d92bf4abae8be3"><code>735f95c</code></a> [GR-78733] Preserve pending return value across finally blocks.</li>
<li><a href="https://github.com/oracle/graaljs/commit/5294bb8331e72cd22dfee22efd952c8b8f55bef2"><code>5294bb8</code></a> Update test262 status.</li>
<li><a href="https://github.com/oracle/graaljs/commit/0f792874c555a8fe5f7649f22b2fc636430634ce"><code>0f79287</code></a> Preserve pending return value across finally blocks.</li>
<li><a href="https://github.com/oracle/graaljs/commit/05e11b88417cbe68955f391d2cc867958d8bb675"><code>05e11b8</code></a> [GR-78732] Avoid prototype mutation during context initialization.</li>
<li><a href="https://github.com/oracle/graaljs/commit/cd3aaaca9180188562116f300a34bf0deb390373"><code>cd3aaac</code></a> Fix and assert functionData.hasStrictFunctionProperties() for async functions.</li>
<li><a href="https://github.com/oracle/graaljs/commit/e09a6d85d8d287897713db87b2e90014079bcbc9"><code>e09a6d8</code></a> Allocate built-in derived constructor functions with their expected prototype.</li>
<li>Additional commits viewable in <a href="https://github.com/oracle/graaljs/compare/vm-24.1.2...vm-25.3.4.1">compare view</a></li>
</ul>
</details>
<br />

<details><summary>Comment — nathanpond, 2026-08-31</summary>

**Held** (2026-08-31): GraalVM js-scriptengine 24 → 25 is a major runtime change for workflow scripts and may require a matching GraalVM/JDK level; not verifiable here without a JDK. Take after archived-96 with the workflow-script E2E specs.

</details>

<details><summary>Comment — nathanpond, 2026-08-31</summary>

Runtime gate after merge (`bd7d1424`), 2026-08-31:
- `infra/ensure-up.sh` rebuilt the Flowable image (build log resolves `js-scriptengine 25.3.4.1`, `polyglot 25.3.4.1`) and recreated the container → healthy.
- Deployed a `scriptTask scriptFormat="javascript"` process over the Flowable REST API and started it with `n=21`: instance ended, variables `doubled=42`, `engine=graaljs-ok` — same result as the GraalJS 24 baseline run beforehand.
- Workflow E2E specs (`WorkflowStudioTests|WorkflowExecutionTests|WorkflowOverrideTests`) → 9/9 against the rebuilt container.
- AutoNate.Web booted against it with no script-task-support warning from `FlowableClient.EnsureJavaScriptScriptTaskSupportAsync`.

</details>

---

## archived-101 — Bump @types/node from 24.13.3 to 26.4.0 in /services/executor

`CLOSED` · app/dependabot · opened 2026-08-31 · `dependabot/npm_and_yarn/services/executor/types/node-26.4.0` → `master`

Bumps [@types/node](https://github.com/DefinitelyTyped/DefinitelyTyped/tree/HEAD/types/node) from 24.13.3 to 26.4.0.
<details>
<summary>Commits</summary>
<ul>
<li>See full diff in <a href="https://github.com/DefinitelyTyped/DefinitelyTyped/commits/HEAD/types/node">compare view</a></li>
</ul>
</details>
<br />


[![Dependabot compatibility score](https://dependabot-badges.githubapp.com/badges/compatibility_score?dependency-name=@types/node&package-manager=npm_and_yarn&previous-version=24.13.3&new-version=26.4.0)](https://docs.github.com/en/github/managing-security-vulnerabilities/about-dependabot-security-updates#about-compatibility-scores)

Dependabot will resolve any conflicts with this PR as long as you don't alter it yourself. You can also trigger a rebase manually by commenting `@dependabot rebase`.

[//]: # (dependabot-automerge-start)
[//]: # (dependabot-automerge-end)

---

<details>
<summary>Dependabot commands and options</summary>
<br />

You can trigger Dependabot actions by commenting on this PR:
- `@dependabot rebase` will rebase this PR
- `@dependabot recreate` will recreate this PR, overwriting any edits that have been made to it
- `@dependabot show <dependency name> ignore conditions` will show all of the ignore conditions of the specified dependency
- `@dependabot ignore this major version` will close this PR and stop Dependabot creating any more for this major version (unless you reopen the PR or upgrade to it yourself)
- `@dependabot ignore this minor version` will close this PR and stop Dependabot creating any more for this minor version (unless you reopen the PR or upgrade to it yourself)
- `@dependabot ignore this dependency` will close this PR and stop Dependabot creating any more for this dependency (unless you reopen the PR or upgrade to it yourself)


</details>

<details><summary>Comment — nathanpond, 2026-08-31</summary>

**Held** (2026-08-31): `@types/node` should track the runtime. Both sidecar images are `node:22-alpine` (`services/*/Dockerfile`); bump this when the base image moves to Node 26.

</details>

<details><summary>Comment — nathanpond, 2026-08-31</summary>

Closing rather than merging: `@types/node` should track the runtime major, and the runtime was just standardised on **Node 24** (archived-139 / archived-140) — the 24.x types already in the lockfile are the correct ones. This bump will be wanted again as a 24 → 26 move once Node 26 enters LTS (October 2026); Dependabot now also tracks the Docker base images so both arrive together.

</details>

<details><summary>Comment — dependabot[bot], 2026-08-31</summary>

OK, I won't notify you again about this release, but will get in touch when a new version is available. If you'd rather skip all updates until the next major or minor version, let me know by commenting `@dependabot ignore this major version` or `@dependabot ignore this minor version`. You can also ignore all major, minor, or patch releases for a dependency by adding an [`ignore` condition](https://docs.github.com/en/code-security/supply-chain-security/configuration-options-for-dependency-updates#ignore) with the desired `update_types` to your config file.

If you change your mind, just re-open this PR and I'll resolve any conflicts on it.

</details>

---

## archived-102 — Bump isolated-vm from 5.0.4 to 7.0.1 in /services/executor

`CLOSED` · app/dependabot · opened 2026-08-31 · `dependabot/npm_and_yarn/services/executor/isolated-vm-7.0.1` → `master`

Bumps [isolated-vm](https://github.com/laverdet/isolated-vm) from 5.0.4 to 7.0.1.
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/laverdet/isolated-vm/commit/699d47a2c9021fbd4953b461bcbca4d7e0e11043"><code>699d47a</code></a> Bump npm version -&gt; 7.0.1</li>
<li><a href="https://github.com/laverdet/isolated-vm/commit/56ae6d7ebd5d6bdf9eac31159ecb720b19de6a5b"><code>56ae6d7</code></a> Fix promise transfer settlement stalling on node 26.4+ (<a href="https://redirect.github.com/laverdet/isolated-vm/issues/565">#565</a>)</li>
<li><a href="https://github.com/laverdet/isolated-vm/commit/38e244bc4e9657ebc93c499b4e1f10034491fae3"><code>38e244b</code></a> Forbid user code in ExternalCopy transfer</li>
<li><a href="https://github.com/laverdet/isolated-vm/commit/e1d371980836972c4ba7d6dc3ecadf5aa9e9e749"><code>e1d3719</code></a> Reduce engine requirement to 24 (<a href="https://redirect.github.com/laverdet/isolated-vm/issues/561">#561</a>)</li>
<li><a href="https://github.com/laverdet/isolated-vm/commit/0ce1de4bf1b8c213736d970d35cfa8b7efdf00fe"><code>0ce1de4</code></a> Link to <code>@isolated-vm/experimental</code></li>
<li><a href="https://github.com/laverdet/isolated-vm/commit/cd0d09593e937b3225dac2118f71d6b709d11b3c"><code>cd0d095</code></a> Fix crash when async instantiate() resolve callback throws synchronously (<a href="https://redirect.github.com/laverdet/isolated-vm/issues/559">#559</a>)</li>
<li><a href="https://github.com/laverdet/isolated-vm/commit/cab912c73a807ca4b741ac78aa8ea255e2f7684a"><code>cab912c</code></a> Maybe fix Windows build</li>
<li><a href="https://github.com/laverdet/isolated-vm/commit/833cd428ea15f4fe635b75545e1632496d73da3e"><code>833cd42</code></a> Console on CI/CD failure</li>
<li><a href="https://github.com/laverdet/isolated-vm/commit/70b82be19d1439ecda898c4d378df7bf82552c45"><code>70b82be</code></a> Disable macOS large strings test</li>
<li><a href="https://github.com/laverdet/isolated-vm/commit/f522ad0c83ffb4ef5c04315ebbfa186257939dde"><code>f522ad0</code></a> README version update</li>
<li>Additional commits viewable in <a href="https://github.com/laverdet/isolated-vm/compare/v5.0.4...v7.0.1">compare view</a></li>
</ul>
</details>
<details>
<summary>Maintainer changes</summary>
<p>This version was pushed to npm by <a href="https://www.npmjs.com/~GitHub%20Actions">GitHub Actions</a>, a new releaser for isolated-vm since your current version.</p>
</details>
<details>
<summary>Install script changes</summary>
<p>This version modifies <code>install</code> script that runs during installation. Review the package contents before updating.</p>
</details>
<br />


[![Dependabot compatibility score](https://dependabot-badges.githubapp.com/badges/compatibility_score?dependency-name=isolated-vm&package-manager=npm_and_yarn&previous-version=5.0.4&new-version=7.0.1)](https://docs.github.com/en/github/managing-security-vulnerabilities/about-dependabot-security-updates#about-compatibility-scores)

Dependabot will resolve any conflicts with this PR as long as you don't alter it yourself. You can also trigger a rebase manually by commenting `@dependabot rebase`.

[//]: # (dependabot-automerge-start)
[//]: # (dependabot-automerge-end)

---

<details>
<summary>Dependabot commands and options</summary>
<br />

You can trigger Dependabot actions by commenting on this PR:
- `@dependabot rebase` will rebase this PR
- `@dependabot recreate` will recreate this PR, overwriting any edits that have been made to it
- `@dependabot show <dependency name> ignore conditions` will show all of the ignore conditions of the specified dependency
- `@dependabot ignore this major version` will close this PR and stop Dependabot creating any more for this major version (unless you reopen the PR or upgrade to it yourself)
- `@dependabot ignore this minor version` will close this PR and stop Dependabot creating any more for this minor version (unless you reopen the PR or upgrade to it yourself)
- `@dependabot ignore this dependency` will close this PR and stop Dependabot creating any more for this dependency (unless you reopen the PR or upgrade to it yourself)


</details>

<details><summary>Comment — nathanpond, 2026-08-31</summary>

**Held** (2026-08-31): `isolated-vm` 7.0.1 declares `engines.node >= 24.0.0`; the executor image is `node:22-alpine`. Bump the base image first (and re-verify the sandbox behaviour — see archived-58).

</details>

<details><summary>Comment — nathanpond, 2026-08-31</summary>

Superseded by archived-140, which moved `isolated-vm` to ^7.0.1 as part of the Node 24 standardisation (archived-139) — 5.x cannot compile against Node 24's V8 headers, so the two had to land together. Verified end-to-end: the rebuilt executor image executes JS transformers through isolated-vm 7.0.1.

</details>

<details><summary>Comment — dependabot[bot], 2026-08-31</summary>

OK, I won't notify you again about this release, but will get in touch when a new version is available. If you'd rather skip all updates until the next major or minor version, let me know by commenting `@dependabot ignore this major version` or `@dependabot ignore this minor version`. You can also ignore all major, minor, or patch releases for a dependency by adding an [`ignore` condition](https://docs.github.com/en/code-security/supply-chain-security/configuration-options-for-dependency-updates#ignore) with the desired `update_types` to your config file.

If you change your mind, just re-open this PR and I'll resolve any conflicts on it.

</details>

---

## archived-103 — Bump pyodide from 0.26.4 to 314.0.6 in /services/executor

`MERGED (merged 2026-08-31)` · app/dependabot · opened 2026-08-31 · `dependabot/npm_and_yarn/services/executor/pyodide-314.0.6` → `master`

Bumps [pyodide](https://github.com/pyodide/pyodide) from 0.26.4 to 314.0.6.
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/pyodide/pyodide/releases">pyodide's releases</a>.</em></p>
<blockquote>
<h2>314.0.6</h2>
<p>See changes at <a href="https://pyodide.org/en/stable/project/changelog.html#version-314-0-6">https://pyodide.org/en/stable/project/changelog.html#version-314-0-6</a></p>
<h2>314.0.5</h2>
<p>Changelog:
<a href="https://pyodide.org/en/stable/project/changelog.html#version-314-0-5">https://pyodide.org/en/stable/project/changelog.html#version-314-0-5</a></p>
<h2>314.0.4</h2>
<p>No release notes provided.</p>
<h2>314.0.3</h2>
<p>No release notes provided.</p>
<h2>314.0.2</h2>
<p>No release notes provided.</p>
<h2>314.0.1</h2>
<p>No release notes provided.</p>
<h2>314.0.0</h2>
<p>No release notes provided.</p>
<h2>314.0.0a2</h2>
<p>No release notes provided.</p>
<h2>314.0.0a1</h2>
<p>No release notes provided.</p>
<h2>0.29.4</h2>
<p>No release notes provided.</p>
<h2>0.29.3</h2>
<p>No release notes provided.</p>
<h2>0.29.2</h2>
<p>No release notes provided.</p>
<h2>0.29.1</h2>
<p>No release notes provided.</p>
<h2>0.29.0</h2>
<p>No release notes provided.</p>
<h2>0.28.3</h2>
<p>No release notes provided.</p>
<h2>0.28.2</h2>
<p>No release notes provided.</p>
<h2>0.28.1</h2>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/pyodide/pyodide/commit/8cec1b9bb8ead68c7c09b0a6443576bec7512268"><code>8cec1b9</code></a> 314.0.6</li>
<li><a href="https://github.com/pyodide/pyodide/commit/a05d1508edba2e97e400e3e240450211ce2df026"><code>a05d150</code></a> 314.0.6</li>
<li><a href="https://github.com/pyodide/pyodide/commit/5ef9c011c7cec358744a202c44dada6ae2803c3a"><code>5ef9c01</code></a> changelog for 314.0.6 release (<a href="https://redirect.github.com/pyodide/pyodide/issues/6439">#6439</a>)</li>
<li><a href="https://github.com/pyodide/pyodide/commit/6973c0d01c9a30b25c247b9024f4deb13a98f3dc"><code>6973c0d</code></a> Rework the union in JsProxy to fix violation of strict aliasing rule (<a href="https://redirect.github.com/pyodide/pyodide/issues/6438">#6438</a>)</li>
<li><a href="https://github.com/pyodide/pyodide/commit/28c30bf6ced8a1de2fbd573dffc052fb6f326b37"><code>28c30bf</code></a> Fix stack switching + tracing/profiling and event loop handling (<a href="https://redirect.github.com/pyodide/pyodide/issues/6437">#6437</a>)</li>
<li><a href="https://github.com/pyodide/pyodide/commit/ef1be45862d670b0562c51f1715c3b8fb6b624cb"><code>ef1be45</code></a> Fix signal handling regression when stack switching (<a href="https://redirect.github.com/pyodide/pyodide/issues/6435">#6435</a>)</li>
<li><a href="https://github.com/pyodide/pyodide/commit/9bdc573b8c3cab9ec51d48cab6f807ccf90e8cb4"><code>9bdc573</code></a> Raise a clear error when loadPackage is given a requirement specifier (<a href="https://redirect.github.com/pyodide/pyodide/issues/6432">#6432</a>)</li>
<li><a href="https://github.com/pyodide/pyodide/commit/b790df39396b6293f68a33a86527ee65909dc950"><code>b790df3</code></a> Update cross-build metadata files for version 314.0.5 (<a href="https://redirect.github.com/pyodide/pyodide/issues/6429">#6429</a>)</li>
<li><a href="https://github.com/pyodide/pyodide/commit/c63e2a80890c66f300044bd35318feb1cd954222"><code>c63e2a8</code></a> Add codeplug.org to related projects list (<a href="https://redirect.github.com/pyodide/pyodide/issues/6417">#6417</a>)</li>
<li><a href="https://github.com/pyodide/pyodide/commit/7da23314b1b7a69b0273166de20a5f0b2dff379c"><code>7da2331</code></a> Link to changelog inside github release (<a href="https://redirect.github.com/pyodide/pyodide/issues/6431">#6431</a>)</li>
<li>Additional commits viewable in <a href="https://github.com/pyodide/pyodide/compare/0.26.4...314.0.6">compare view</a></li>
</ul>
</details>
<details>
<summary>Maintainer changes</summary>
<p>This version was pushed to npm by <a href="https://www.npmjs.com/~CircleCI">CircleCI</a>, a new releaser for pyodide since your current version.</p>
</details>
<br />

<details><summary>Comment — nathanpond, 2026-08-31</summary>

**Held** (2026-08-31): Pyodide moved to CPython-based version numbers, so 0.26.4 → 314.0.6 is a real release but a major upgrade of the Python sandbox runtime. The executor has no automated tests and open sandbox issues (archived-58, archived-64); take this deliberately alongside that work, not as an unreviewed bump.

</details>

<details><summary>Comment — nathanpond, 2026-08-31</summary>

**Re-examined 2026-08-31 — validating and merging.** The hold said: *"the executor has no automated tests and open sandbox issues (archived-58, archived-64); take this deliberately alongside that work."* That precondition is now met — archived-58, archived-64 and archived-161 are fixed, and the executor has an 11-test sandbox suite whose whole purpose is to answer a question like this one.

**Local suite against `pyodide@314.0.6`: 11 passed / 0 failed**, including every property the sandbox rests on — timeout (plain busy loop *and* one swallowing `KeyboardInterrupt`), the `memoryMb` cap, per-request isolation, the host-escape probes, and the fixed environment.

The escape probes fail for the **right** reasons, not incidentally:

```
blocked js.process       AttributeError: process
blocked js.eval          AttributeError: eval
blocked pyodide_js       ModuleNotFoundError: No module named 'pyodide_js'
blocked run_js           ImportError: cannot import name 'eval' from 'js'
blocked open /etc/passwd FileNotFoundError: [Errno 44] ...
memcap                   MemoryError
```

That last line matters most: the `memoryMb` cap reads `pyodide._module.HEAPU8` to size the baseline, which is a **private** API and the thing most likely to break across a major runtime jump. It still works — verified by an over-allocation actually raising `MemoryError` rather than the cap silently going unenforced.

**Container verified too**, not just the local build: image builds clean, and running it on the compose network against NATS gives `js OK`, `python OK`, `py-timeout OK` ("timed out after 2000ms"), `py-escape OK`, and it keeps serving afterwards. The traceback path reads `/lib/python314.zip`, confirming the interpreter actually moved.

**Operator note:** this is Python **3.12 → 3.14** for author-written transformers, not just a package bump. Existing Python transformers that rely on stdlib removed in 3.13/3.14, or on 3.12-specific behaviour, can break — that is a data-migration consideration, not something the test suite can cover. Package size is unchanged (13 MB).

</details>

---

## archived-104 — Bump the hocuspocus-minor-patch group across 1 directory with 10 updates

`CLOSED` · app/dependabot · opened 2026-08-31 · `dependabot/npm_and_yarn/services/hocuspocus/hocuspocus-minor-patch-fad54ada6e` → `master`

Bumps the hocuspocus-minor-patch group with 10 updates in the /services/hocuspocus directory:

| Package | From | To |
| --- | --- | --- |
| [@blocknote/core](https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/core) | `0.51.0` | `0.54.0` |
| [@blocknote/server-util](https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/server-util) | `0.51.0` | `0.54.0` |
| [@hocuspocus/server](https://github.com/ueberdosis/hocuspocus) | `4.0.0` | `4.6.0` |
| [pg](https://github.com/brianc/node-postgres/tree/HEAD/packages/pg) | `8.20.0` | `8.23.0` |
| [@types/pg](https://github.com/DefinitelyTyped/DefinitelyTyped/tree/HEAD/types/pg) | `8.20.0` | `8.23.1` |
| [react](https://github.com/react/react/tree/HEAD/packages/react) | `19.2.6` | `19.2.8` |
| [@types/react](https://github.com/DefinitelyTyped/DefinitelyTyped/tree/HEAD/types/react) | `19.2.14` | `19.2.18` |
| [react-dom](https://github.com/react/react/tree/HEAD/packages/react-dom) | `19.2.6` | `19.2.8` |
| [@types/react-dom](https://github.com/DefinitelyTyped/DefinitelyTyped/tree/HEAD/types/react-dom) | `19.2.3` | `19.2.5` |
| [yjs](https://github.com/yjs/yjs) | `13.6.30` | `13.6.32` |


Updates `@blocknote/core` from 0.51.0 to 0.54.0
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/TypeCellOS/BlockNote/releases">@​blocknote/core's releases</a>.</em></p>
<blockquote>
<h2>v0.54.0</h2>
<h2>0.54.0 (2026-08-13)</h2>
<p>💖 The math block and diagram block has been sponsored by <a href="https://www.numerique.gouv.fr/dinum/">DINUM</a> 🇫🇷</p>
<h3>Math Block</h3>
<p>A long requested feature, you can now add block &amp; inline math to a BlockNote editor. They are driven by <a href="https://katex.org/">Katex</a> &amp; support much of <a href="https://www.latex-project.org/">Latex</a> for all your notation needs.</p>
<p><a href="https://github.com/user-attachments/assets/8fb5790e-6922-4f02-a35f-27c791b877e8">https://github.com/user-attachments/assets/8fb5790e-6922-4f02-a35f-27c791b877e8</a></p>
<p><a href="https://www.blocknotejs.org/examples/custom-schema/math-block">Link to demo</a></p>
<h3>Diagram Block</h3>
<p>We've also added support for a diagram block driven by <a href="https://mermaid.js.org/">Mermaid.js</a>, allowing you to add diagramming to the editor.</p>
<p><a href="https://github.com/user-attachments/assets/0a64e98a-5bf0-4dec-b1a4-84ccf98f4a70">https://github.com/user-attachments/assets/0a64e98a-5bf0-4dec-b1a4-84ccf98f4a70</a></p>
<p><a href="https://www.blocknotejs.org/examples/custom-schema/diagram-block">Link to demo</a></p>
<h3>Source Block with Preview</h3>
<p>Both the Math block &amp; Diagram block are built on a primitive that you can build your own custom blocks from. The Source Block with Preview primitive allows you to build a pair of a block which renders content with an inline editor for the content being rendered. This can enable other sorts of preview-like features in the future, exposed as an API for you to build your own custom blocks with.</p>
<!-- raw HTML omitted -->
<!-- raw HTML omitted -->
<p><a href="https://www.blocknotejs.org/examples/custom-schema/source-with-preview">Link to demo</a></p>
<h3>🚀 Features</h3>
<ul>
<li>Adds a Math block (<a href="https://github.com/TypeCellOS/BlockNote/commit/2a34f7d70">2a34f7d70</a>)</li>
<li>Adds a Diagram block (<a href="https://github.com/TypeCellOS/BlockNote/commit/0fca0ee7a">0fca0ee7a</a>)</li>
<li><strong>core:</strong> Source-with-preview, syntax highlighting &amp; exporter images (<a href="https://github.com/TypeCellOS/BlockNote/commit/503c796d3">503c796d3</a>)</li>
</ul>
<h3>🩹 Fixes</h3>
<ul>
<li><strong>ai:</strong> Operations on collaborative documents (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2952">#2952</a>)</li>
<li><strong>ai:</strong> Operations on blocks containing comments (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2953">#2953</a>)</li>
<li><strong>pdf:</strong> Add custom font and fontFamily options for CJK (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2945">#2945</a>)</li>
<li>Expose first suggestion as active descendant (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2965">#2965</a>)</li>
<li><strong>xl-docx-exporter:</strong> Clamp list nesting to the levels DOCX defines (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2969">#2969</a>)</li>
</ul>
<h3>❤️ Thank You</h3>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Changelog</summary>
<p><em>Sourced from <a href="https://github.com/TypeCellOS/BlockNote/blob/main/CHANGELOG.md">@​blocknote/core's changelog</a>.</em></p>
<blockquote>
<h2>0.54.0 (2026-08-13)</h2>
<h3>🚀 Features</h3>
<ul>
<li>Adds a Math block (<a href="https://github.com/TypeCellOS/BlockNote/commit/2a34f7d70">2a34f7d70</a>)</li>
<li>Adds a Diagram block (<a href="https://github.com/TypeCellOS/BlockNote/commit/0fca0ee7a">0fca0ee7a</a>)</li>
<li><strong>core:</strong> Source-with-preview, syntax highlighting &amp; exporter images (<a href="https://github.com/TypeCellOS/BlockNote/commit/503c796d3">503c796d3</a>)</li>
</ul>
<h3>🩹 Fixes</h3>
<ul>
<li><strong>ai:</strong> Operations on collaborative documents (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2952">#2952</a>)</li>
<li><strong>ai:</strong> Operations on blocks containing comments (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2953">#2953</a>)</li>
<li><strong>pdf:</strong> Add custom font and fontFamily options for CJK (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2945">#2945</a>)</li>
<li>Expose first suggestion as active descendant (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2965">#2965</a>)</li>
<li><strong>xl-docx-exporter:</strong> Clamp list nesting to the levels DOCX defines (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2969">#2969</a>)</li>
</ul>
<h3>❤️ Thank You</h3>
<ul>
<li>Adarshsm <a href="mailto:adarshmudugal@gmail.com">adarshmudugal@gmail.com</a></li>
<li>Nick The Sick (<a href="https://github.com/nperez0111"><code>@​nperez0111</code></a>)</li>
<li>Pupuking723 <a href="mailto:2318857637@qq.com">2318857637@qq.com</a></li>
</ul>
<h2>0.53.0 (2026-08-06)</h2>
<h3>🚀 Features</h3>
<ul>
<li><strong>shadcn:</strong> ⚠️ Use base-ui instead of radix (BLO-1279) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2913">#2913</a>)</li>
</ul>
<h3>🩹 Fixes</h3>
<ul>
<li>getCellSelection throwing error in positions (BLO-1193) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2911">#2911</a>)</li>
<li>Multi-column slash menu items within a column (BLO-905) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2914">#2914</a>)</li>
<li>Suggestion menu behaviour (BLO-1283, BLO-955) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2930">#2930</a>)</li>
<li>Ignore useless block/inline content mutations (BLO-1224) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2912">#2912</a>)</li>
<li><strong>slash-menu:</strong> Better overflow behavior (BLO-1192) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2909">#2909</a>)</li>
<li>Slash menu item selection behaviour (BLO-1222) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2838">#2838</a>)</li>
<li>HTML export/parse round trip ignoring empty blocks (BLO-873) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2931">#2931</a>)</li>
<li><strong>core:</strong> Guard getBlock() calls to prevent TypeError on stale blocks (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2941">#2941</a>)</li>
<li>Stop stale node view positions crashing the editor (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2938">#2938</a>)</li>
<li>Multi-column trailing blocks, column hover borders &amp; drop cursor left edge BLO-1226 (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2885">#2885</a>)</li>
</ul>
<h4>⚠️ Breaking Changes</h4>
<ul>
<li><strong>shadcn:</strong> ⚠️ Use base-ui instead of radix (BLO-1279) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2913">#2913</a>)</li>
</ul>
<h3>❤️ Thank You</h3>
<ul>
<li>Yousef</li>
<li>Nick Perez <a href="mailto:nick@blocknotejs.org">nick@blocknotejs.org</a></li>
<li>Matthew Lipski (<a href="https://github.com/matthewlipski"><code>@​matthewlipski</code></a>)</li>
</ul>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/ea5d80358f179d1683abcd2e0e3e9d547bf52eef"><code>ea5d803</code></a> chore(release): v0.54.0</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/503c796d37f2c8734cf65e9bad3348127043c63b"><code>503c796</code></a> feat(core): source-with-preview, syntax highlighting &amp; exporter images</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/99253c3814a93e6f5d1ae318efeb0b10df90f32d"><code>99253c3</code></a> chore: migrate to TypeScript 7 and consolidate the <a href="https://github.com/shared"><code>@​shared</code></a> alias</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/bea469e31eab19242b1238cd3600a14c1d6148c1"><code>bea469e</code></a> refactor: vendor <code>@​tanstack/store</code> as a first-party Store (<a href="https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/core/issues/2956">#2956</a>)</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/dee3401a2647eb01b7a982b32e98e0bd182713fe"><code>dee3401</code></a> chore: bump prosemirror-view to ^1.42.2 (<a href="https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/core/issues/2954">#2954</a>)</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/decb3d21480ceed983d3befb4e87ff8d26bcc938"><code>decb3d2</code></a> fix(ai): operations on blocks containing comments (<a href="https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/core/issues/2953">#2953</a>)</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/824abce757ed1a44e4dbb048fe88ea954b592831"><code>824abce</code></a> fix(ai): operations on collaborative documents (<a href="https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/core/issues/2952">#2952</a>)</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/529c3b02f6e413c362e96718dd712dd4b4c495a0"><code>529c3b0</code></a> chore(release): v0.53.0</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/d998f0168abd54ec57239479ea2dfc3d17df6a1a"><code>d998f01</code></a> fix: multi-column trailing blocks, column hover borders &amp; drop cursor left ed...</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/58d43ff08806ce078f03cf5a28afeefb1bede482"><code>58d43ff</code></a> fix: stop stale node view positions crashing the editor (<a href="https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/core/issues/2938">#2938</a>)</li>
<li>Additional commits viewable in <a href="https://github.com/TypeCellOS/BlockNote/commits/v0.54.0/packages/core">compare view</a></li>
</ul>
</details>
<br />

Updates `@blocknote/server-util` from 0.51.0 to 0.54.0
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/TypeCellOS/BlockNote/releases">@​blocknote/server-util's releases</a>.</em></p>
<blockquote>
<h2>v0.54.0</h2>
<h2>0.54.0 (2026-08-13)</h2>
<p>💖 The math block and diagram block has been sponsored by <a href="https://www.numerique.gouv.fr/dinum/">DINUM</a> 🇫🇷</p>
<h3>Math Block</h3>
<p>A long requested feature, you can now add block &amp; inline math to a BlockNote editor. They are driven by <a href="https://katex.org/">Katex</a> &amp; support much of <a href="https://www.latex-project.org/">Latex</a> for all your notation needs.</p>
<p><a href="https://github.com/user-attachments/assets/8fb5790e-6922-4f02-a35f-27c791b877e8">https://github.com/user-attachments/assets/8fb5790e-6922-4f02-a35f-27c791b877e8</a></p>
<p><a href="https://www.blocknotejs.org/examples/custom-schema/math-block">Link to demo</a></p>
<h3>Diagram Block</h3>
<p>We've also added support for a diagram block driven by <a href="https://mermaid.js.org/">Mermaid.js</a>, allowing you to add diagramming to the editor.</p>
<p><a href="https://github.com/user-attachments/assets/0a64e98a-5bf0-4dec-b1a4-84ccf98f4a70">https://github.com/user-attachments/assets/0a64e98a-5bf0-4dec-b1a4-84ccf98f4a70</a></p>
<p><a href="https://www.blocknotejs.org/examples/custom-schema/diagram-block">Link to demo</a></p>
<h3>Source Block with Preview</h3>
<p>Both the Math block &amp; Diagram block are built on a primitive that you can build your own custom blocks from. The Source Block with Preview primitive allows you to build a pair of a block which renders content with an inline editor for the content being rendered. This can enable other sorts of preview-like features in the future, exposed as an API for you to build your own custom blocks with.</p>
<!-- raw HTML omitted -->
<!-- raw HTML omitted -->
<p><a href="https://www.blocknotejs.org/examples/custom-schema/source-with-preview">Link to demo</a></p>
<h3>🚀 Features</h3>
<ul>
<li>Adds a Math block (<a href="https://github.com/TypeCellOS/BlockNote/commit/2a34f7d70">2a34f7d70</a>)</li>
<li>Adds a Diagram block (<a href="https://github.com/TypeCellOS/BlockNote/commit/0fca0ee7a">0fca0ee7a</a>)</li>
<li><strong>core:</strong> Source-with-preview, syntax highlighting &amp; exporter images (<a href="https://github.com/TypeCellOS/BlockNote/commit/503c796d3">503c796d3</a>)</li>
</ul>
<h3>🩹 Fixes</h3>
<ul>
<li><strong>ai:</strong> Operations on collaborative documents (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2952">#2952</a>)</li>
<li><strong>ai:</strong> Operations on blocks containing comments (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2953">#2953</a>)</li>
<li><strong>pdf:</strong> Add custom font and fontFamily options for CJK (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2945">#2945</a>)</li>
<li>Expose first suggestion as active descendant (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2965">#2965</a>)</li>
<li><strong>xl-docx-exporter:</strong> Clamp list nesting to the levels DOCX defines (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2969">#2969</a>)</li>
</ul>
<h3>❤️ Thank You</h3>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Changelog</summary>
<p><em>Sourced from <a href="https://github.com/TypeCellOS/BlockNote/blob/main/CHANGELOG.md">@​blocknote/server-util's changelog</a>.</em></p>
<blockquote>
<h2>0.54.0 (2026-08-13)</h2>
<h3>🚀 Features</h3>
<ul>
<li>Adds a Math block (<a href="https://github.com/TypeCellOS/BlockNote/commit/2a34f7d70">2a34f7d70</a>)</li>
<li>Adds a Diagram block (<a href="https://github.com/TypeCellOS/BlockNote/commit/0fca0ee7a">0fca0ee7a</a>)</li>
<li><strong>core:</strong> Source-with-preview, syntax highlighting &amp; exporter images (<a href="https://github.com/TypeCellOS/BlockNote/commit/503c796d3">503c796d3</a>)</li>
</ul>
<h3>🩹 Fixes</h3>
<ul>
<li><strong>ai:</strong> Operations on collaborative documents (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2952">#2952</a>)</li>
<li><strong>ai:</strong> Operations on blocks containing comments (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2953">#2953</a>)</li>
<li><strong>pdf:</strong> Add custom font and fontFamily options for CJK (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2945">#2945</a>)</li>
<li>Expose first suggestion as active descendant (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2965">#2965</a>)</li>
<li><strong>xl-docx-exporter:</strong> Clamp list nesting to the levels DOCX defines (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2969">#2969</a>)</li>
</ul>
<h3>❤️ Thank You</h3>
<ul>
<li>Adarshsm <a href="mailto:adarshmudugal@gmail.com">adarshmudugal@gmail.com</a></li>
<li>Nick The Sick (<a href="https://github.com/nperez0111"><code>@​nperez0111</code></a>)</li>
<li>Pupuking723 <a href="mailto:2318857637@qq.com">2318857637@qq.com</a></li>
</ul>
<h2>0.53.0 (2026-08-06)</h2>
<h3>🚀 Features</h3>
<ul>
<li><strong>shadcn:</strong> ⚠️ Use base-ui instead of radix (BLO-1279) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2913">#2913</a>)</li>
</ul>
<h3>🩹 Fixes</h3>
<ul>
<li>getCellSelection throwing error in positions (BLO-1193) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2911">#2911</a>)</li>
<li>Multi-column slash menu items within a column (BLO-905) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2914">#2914</a>)</li>
<li>Suggestion menu behaviour (BLO-1283, BLO-955) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2930">#2930</a>)</li>
<li>Ignore useless block/inline content mutations (BLO-1224) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2912">#2912</a>)</li>
<li><strong>slash-menu:</strong> Better overflow behavior (BLO-1192) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2909">#2909</a>)</li>
<li>Slash menu item selection behaviour (BLO-1222) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2838">#2838</a>)</li>
<li>HTML export/parse round trip ignoring empty blocks (BLO-873) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2931">#2931</a>)</li>
<li><strong>core:</strong> Guard getBlock() calls to prevent TypeError on stale blocks (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2941">#2941</a>)</li>
<li>Stop stale node view positions crashing the editor (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2938">#2938</a>)</li>
<li>Multi-column trailing blocks, column hover borders &amp; drop cursor left edge BLO-1226 (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2885">#2885</a>)</li>
</ul>
<h4>⚠️ Breaking Changes</h4>
<ul>
<li><strong>shadcn:</strong> ⚠️ Use base-ui instead of radix (BLO-1279) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2913">#2913</a>)</li>
</ul>
<h3>❤️ Thank You</h3>
<ul>
<li>Yousef</li>
<li>Nick Perez <a href="mailto:nick@blocknotejs.org">nick@blocknotejs.org</a></li>
<li>Matthew Lipski (<a href="https://github.com/matthewlipski"><code>@​matthewlipski</code></a>)</li>
</ul>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/ea5d80358f179d1683abcd2e0e3e9d547bf52eef"><code>ea5d803</code></a> chore(release): v0.54.0</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/99253c3814a93e6f5d1ae318efeb0b10df90f32d"><code>99253c3</code></a> chore: migrate to TypeScript 7 and consolidate the <a href="https://github.com/shared"><code>@​shared</code></a> alias</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/529c3b02f6e413c362e96718dd712dd4b4c495a0"><code>529c3b0</code></a> chore(release): v0.53.0</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/58d43ff08806ce078f03cf5a28afeefb1bede482"><code>58d43ff</code></a> fix: stop stale node view positions crashing the editor (<a href="https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/server-util/issues/2938">#2938</a>)</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/dee7880b89b1e9bc00b4f4481f32652c7a4b4408"><code>dee7880</code></a> chore(release): v0.52.1</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/a99aab441b5db07c35d9f5ce406ea1676c6314ca"><code>a99aab4</code></a> chore(release): v0.52.0</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/75f3e6aefe19960fa3692bc9fb4bbbb587151d99"><code>75f3e6a</code></a> chore: misc/cleanup</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/c916f2cdce40be93a9f8a25c096445f57df79124"><code>c916f2c</code></a> fix: exclude tsbuildinfo from build task output to fix cache misses</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/95f0b93b6e9610542d10e64d4e119a24cff01646"><code>95f0b93</code></a> chore: migrate to tsgo for type declarations, enable type-aware oxlint</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/9fb13a7ccaafbc88ee80c0106e6e066aff116549"><code>9fb13a7</code></a> fix: add output field to vp build tasks so cache restores dist files</li>
<li>Additional commits viewable in <a href="https://github.com/TypeCellOS/BlockNote/commits/v0.54.0/packages/server-util">compare view</a></li>
</ul>
</details>
<br />

Updates `@hocuspocus/server` from 4.0.0 to 4.6.0
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/ueberdosis/hocuspocus/releases">@​hocuspocus/server's releases</a>.</em></p>
<blockquote>
<h2>v4.6.0</h2>
<p>extension-redis will now slightly (setImmediate) delay forwarding messages to Redis, which improves performance a lot when many (500+) users are connected to the same document.</p>
<h2>What's Changed</h2>
<ul>
<li>feat/redis pending flushes by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1135">ueberdosis/hocuspocus#1135</a></li>
<li>fix: encode stateless message once when received operation via Redis … by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1136">ueberdosis/hocuspocus#1136</a></li>
</ul>
<p><strong>Full Changelog</strong>: <a href="https://github.com/ueberdosis/hocuspocus/compare/v4.5.0...v4.6.0">https://github.com/ueberdosis/hocuspocus/compare/v4.5.0...v4.6.0</a></p>
<h2>v4.5.0</h2>
<h2>What's Changed</h2>
<ul>
<li>feat: batch updates before sending to clients by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1130">ueberdosis/hocuspocus#1130</a></li>
<li>fix: ignore message in awarenessUpdateHandler if origin=this by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1129">ueberdosis/hocuspocus#1129</a></li>
<li>fix: when beforeHandleMessage throws, we don't want to process other messages that were already queued by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1123">ueberdosis/hocuspocus#1123</a></li>
</ul>
<p><strong>Full Changelog</strong>: <a href="https://github.com/ueberdosis/hocuspocus/compare/v4.4.0...v4.5.0">https://github.com/ueberdosis/hocuspocus/compare/v4.4.0...v4.5.0</a></p>
<h2>v4.4.0</h2>
<h2>What's Changed</h2>
<ul>
<li>feat: add <code>flushDelay</code> option for batching updates to reduce websocket traffic during heavy editing by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1118">ueberdosis/hocuspocus#1118</a></li>
<li>feat: add consistent state synchronization across Redis instances by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1119">ueberdosis/hocuspocus#1119</a></li>
<li>fix: make sure server.destroy() only runs once by <a href="https://github.com/DefV"><code>@​DefV</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1114">ueberdosis/hocuspocus#1114</a></li>
<li>fix: allow binding the server to a specific address by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1121">ueberdosis/hocuspocus#1121</a></li>
<li>build(deps): bump actions/checkout from 6 to 7 by <a href="https://github.com/dependabot"><code>@​dependabot</code></a>[bot] in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1117">ueberdosis/hocuspocus#1117</a></li>
<li>build(deps): bump hono from 4.12.21 to 4.12.25 by <a href="https://github.com/dependabot"><code>@​dependabot</code></a>[bot] in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1116">ueberdosis/hocuspocus#1116</a></li>
<li>build(deps): bump ws from 8.19.0 to 8.21.0 by <a href="https://github.com/dependabot"><code>@​dependabot</code></a>[bot] in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1115">ueberdosis/hocuspocus#1115</a></li>
</ul>
<h2>New Contributors</h2>
<ul>
<li><a href="https://github.com/DefV"><code>@​DefV</code></a> made their first contribution in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1114">ueberdosis/hocuspocus#1114</a></li>
</ul>
<p><strong>Full Changelog</strong>: <a href="https://github.com/ueberdosis/hocuspocus/compare/v4.3.0...v4.4.0">https://github.com/ueberdosis/hocuspocus/compare/v4.3.0...v4.4.0</a></p>
<h2>v4.3.0</h2>
<h2>What's Changed</h2>
<ul>
<li>feat: add <code>afterHandleMessage</code> hook to run after message handling completion by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1112">ueberdosis/hocuspocus#1112</a></li>
<li>feat: enforce pre-auth resource limits to safeguard server stability by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1113">ueberdosis/hocuspocus#1113</a></li>
</ul>
<p><strong>Full Changelog</strong>: <a href="https://github.com/ueberdosis/hocuspocus/compare/v4.2.0...v4.3.0">https://github.com/ueberdosis/hocuspocus/compare/v4.2.0...v4.3.0</a></p>
<h2>v4.2.0</h2>
<h2>What's Changed</h2>
<ul>
<li>feat: add <code>unloadImmediately</code> option to <code>disconnect()</code> for configurable document persistence behavior by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1111">ueberdosis/hocuspocus#1111</a></li>
</ul>
<p><strong>Full Changelog</strong>: <a href="https://github.com/ueberdosis/hocuspocus/compare/v4.1.2...v4.2.0">https://github.com/ueberdosis/hocuspocus/compare/v4.1.2...v4.2.0</a></p>
<h2>v4.1.2</h2>
<h2>What's Changed</h2>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Changelog</summary>
<p><em>Sourced from <a href="https://github.com/ueberdosis/hocuspocus/blob/main/CHANGELOG.md">@​hocuspocus/server's changelog</a>.</em></p>
<blockquote>
<h1><a href="https://github.com/ueberdosis/hocuspocus/compare/v4.5.0...v4.6.0">4.6.0</a> (2026-08-10)</h1>
<h3>Bug Fixes</h3>
<ul>
<li>encode stateless message once when received operation via Redis ; this is a performance fix. (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1136">#1136</a>) (<a href="https://github.com/ueberdosis/hocuspocus/commit/b524b4b30299a64ffa1309f70a0fd6e761103d4a">b524b4b</a>)</li>
</ul>
<h1><a href="https://github.com/ueberdosis/hocuspocus/compare/v4.4.0...v4.5.0">4.5.0</a> (2026-08-04)</h1>
<h3>Bug Fixes</h3>
<ul>
<li>audit (<a href="https://github.com/ueberdosis/hocuspocus/commit/141360c256022deb5578c3902c3dfe0af8f6516e">141360c</a>)</li>
<li>flawky test relying on timings (<a href="https://github.com/ueberdosis/hocuspocus/commit/fe4a8e68801f1659624f53da745e595ad9f11c63">fe4a8e6</a>)</li>
<li>ignore message in awarenessUpdateHandler if origin=this (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1129">#1129</a>) (<a href="https://github.com/ueberdosis/hocuspocus/commit/08b25d4b258d932c68c999c14edcb4efc65c7a9b">08b25d4</a>)</li>
<li>update packages via audit --fix (<a href="https://github.com/ueberdosis/hocuspocus/commit/1dc9ca0ff35f1033136473d134cee8cb6b336281">1dc9ca0</a>)</li>
<li>when beforeHandleMessage throws, we don't want to process other messages that were already queued (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1123">#1123</a>) (<a href="https://github.com/ueberdosis/hocuspocus/commit/ed5dc40581cc829a6d0b04040717a8ee89296140">ed5dc40</a>)</li>
</ul>
<h3>Features</h3>
<ul>
<li>pnpm11 (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1133">#1133</a>) (<a href="https://github.com/ueberdosis/hocuspocus/commit/01c224ad9133340048c0e4f7bdce3981f4984d76">01c224a</a>)</li>
</ul>
<h1><a href="https://github.com/ueberdosis/hocuspocus/compare/v4.3.0...v4.4.0">4.4.0</a> (2026-07-13)</h1>
<h3>Bug Fixes</h3>
<ul>
<li>allow binding the server to a specific address (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1121">#1121</a>) (<a href="https://github.com/ueberdosis/hocuspocus/commit/408127b1c090356cc9148a801f314a8e6f863b09">408127b</a>)</li>
</ul>
<h3>Features</h3>
<ul>
<li>add <code>flushDelay</code> option for batching updates to reduce websocket traffic during heavy editing (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1118">#1118</a>) (<a href="https://github.com/ueberdosis/hocuspocus/commit/75594c05d57d48f2f70d4c9440c28b8226bf95ac">75594c0</a>)</li>
<li>add consistent state synchronization across Redis instances (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1119">#1119</a>) (<a href="https://github.com/ueberdosis/hocuspocus/commit/0051a6cb7618290d1f574da7ad61da2be77f839d">0051a6c</a>)</li>
</ul>
<h1><a href="https://github.com/ueberdosis/hocuspocus/compare/v4.2.0...v4.3.0">4.3.0</a> (2026-06-18)</h1>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/5c85b91af99544630200c438bfc5594a574d912e"><code>5c85b91</code></a> v4.6.0</li>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/d55367e6d3c20167d1daf920aa1e1094909a58ba"><code>d55367e</code></a> Feat/redis pending flushes (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1135">#1135</a>)</li>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/b524b4b30299a64ffa1309f70a0fd6e761103d4a"><code>b524b4b</code></a> fix: encode stateless message once when received operation via Redis ; this i...</li>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/3ec608445b8e024e15759504cca9ff1f7b09edf8"><code>3ec6084</code></a> build(deps): bump pnpm/action-setup from 5 to 6.0.9 (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1131">#1131</a>)</li>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/7827bded7c9181513a3b7c94acbaee0e4059d066"><code>7827bde</code></a> v4.5.0</li>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/141360c256022deb5578c3902c3dfe0af8f6516e"><code>141360c</code></a> fix: audit</li>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/1dc9ca0ff35f1033136473d134cee8cb6b336281"><code>1dc9ca0</code></a> fix: update packages via audit --fix</li>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/01c224ad9133340048c0e4f7bdce3981f4984d76"><code>01c224a</code></a> feat: pnpm11 (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1133">#1133</a>)</li>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/d9f87a6b738afa718dc0dd47580e02eacc764ce8"><code>d9f87a6</code></a> Feat/batch updates before sending to clients (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1130">#1130</a>)</li>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/a5812e6ec2fbeeefd6dd388a39e1d16fd192f6db"><code>a5812e6</code></a> chore: sync default port with playground</li>
<li>Additional commits viewable in <a href="https://github.com/ueberdosis/hocuspocus/compare/v4.0.0...v4.6.0">compare view</a></li>
</ul>
</details>
<br />

Updates `pg` from 8.20.0 to 8.23.0
<details>
<summary>Changelog</summary>
<p><em>Sourced from <a href="https://github.com/brianc/node-postgres/blob/master/CHANGELOG.md">pg's changelog</a>.</em></p>
<blockquote>
<h2>pg@8.23.0</h2>
<ul>
<li>Add support for query <a href="https://redirect.github.com/brianc/node-postgres/pull/3652"><code>pipelineing</code></a>.</li>
</ul>
<h2>pg@8.22.0</h2>
<ul>
<li>Add support for <a href="https://redirect.github.com/brianc/node-postgres/pull/3688">sslnegotiation=direct</a> for PostgreSQL 17+.</li>
</ul>
<h2>pg@8.21.0</h2>
<ul>
<li>Handle <a href="https://redirect.github.com/brianc/node-postgres/pull/3521">SASL SCRAM</a> server error responses properly.</li>
<li>Add support for <a href="https://redirect.github.com/brianc/node-postgres/pull/3667">node@26</a>.</li>
<li>Add <code>scramMaxIterations</code> <a href="https://redirect.github.com/brianc/node-postgres/pull/3677">config option</a>.</li>
<li>Add <code>client.getTransactionStatus()</code> <a href="https://redirect.github.com/brianc/node-postgres/pull/3645">method</a>.</li>
</ul>
</blockquote>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/brianc/node-postgres/commit/df274d1ba9ad9d11a8f1079314faeafde7208207"><code>df274d1</code></a> Publish</li>
<li><a href="https://github.com/brianc/node-postgres/commit/eb19d0fe6d7da11e7f1c5e73e4026350e42f9156"><code>eb19d0f</code></a> Add opt-in query pipelining (<a href="https://github.com/brianc/node-postgres/tree/HEAD/packages/pg/issues/3652">#3652</a>)</li>
<li><a href="https://github.com/brianc/node-postgres/commit/b617619f9fb6fbd231731823e2732a2927ded4be"><code>b617619</code></a> Publish</li>
<li><a href="https://github.com/brianc/node-postgres/commit/d80b2612fbe83ed8234637f20b943d85e4331094"><code>d80b261</code></a> Update docs &amp; changelog</li>
<li><a href="https://github.com/brianc/node-postgres/commit/835fb83ab9e1cf30fa8367ba42bd633720d71832"><code>835fb83</code></a> Fix error handling for exceptions on values parsing. (<a href="https://github.com/brianc/node-postgres/tree/HEAD/packages/pg/issues/3574">#3574</a>)</li>
<li><a href="https://github.com/brianc/node-postgres/commit/f49ab4a9795ae0866409f9bfe52a68b4f65ef024"><code>f49ab4a</code></a> fix: correct spelling mistakes across codebase (<a href="https://github.com/brianc/node-postgres/tree/HEAD/packages/pg/issues/3692">#3692</a>)</li>
<li><a href="https://github.com/brianc/node-postgres/commit/d7175a4aa0347b7416109e9ecc61d4d235486d0e"><code>d7175a4</code></a> Expand CI matrix of PG versions and add direct SSL test (<a href="https://github.com/brianc/node-postgres/tree/HEAD/packages/pg/issues/3693">#3693</a>)</li>
<li><a href="https://github.com/brianc/node-postgres/commit/882fc308cce7bf136cd1448e00395f760dad3e00"><code>882fc30</code></a> Add support for sslnegotiation=direct (PostgreSQL 17) (<a href="https://github.com/brianc/node-postgres/tree/HEAD/packages/pg/issues/3688">#3688</a>)</li>
<li><a href="https://github.com/brianc/node-postgres/commit/544b1ce8152bc280e398dc1e8a66920abe6a640e"><code>544b1ce</code></a> Publish</li>
<li><a href="https://github.com/brianc/node-postgres/commit/cc03fa5cdf0f1e67b2518ebad5cf2269206aa49c"><code>cc03fa5</code></a> Add scramMaxIterations option to limit SCRAM iteration count (<a href="https://github.com/brianc/node-postgres/tree/HEAD/packages/pg/issues/3677">#3677</a>)</li>
<li>Additional commits viewable in <a href="https://github.com/brianc/node-postgres/commits/pg@8.23.0/packages/pg">compare view</a></li>
</ul>
</details>
<br />

Updates `@types/pg` from 8.20.0 to 8.23.1
<details>
<summary>Commits</summary>
<ul>
<li>See full diff in <a href="https://github.com/DefinitelyTyped/DefinitelyTyped/commits/HEAD/types/pg">compare view</a></li>
</ul>
</details>
<br />

Updates `react` from 19.2.6 to 19.2.8
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/react/react/releases">react's releases</a>.</em></p>
<blockquote>
<h2>19.2.8 (July 21st, 2026)</h2>
<h2>React Server Components</h2>
<ul>
<li>Performance improvements when decoding
(<a href="https://redirect.github.com/facebook/react/pull/37087">#37087</a> by <a href="https://github.com/eps1lon"><code>@​eps1lon</code></a>)</li>
</ul>
<h2>19.2.7 (June 1st, 2026)</h2>
<h2>React Server Components</h2>
<ul>
<li>Fixed missing <code>FormData</code> entries in Server Actions which regressed in 19.2.6
(<a href="https://redirect.github.com/facebook/react/pull/36566">#36566</a> by <a href="https://github.com/unstubbable"><code>@​unstubbable</code></a>)</li>
</ul>
</blockquote>
</details>
<details>
<summary>Changelog</summary>
<p><em>Sourced from <a href="https://github.com/react/react/blob/main/CHANGELOG.md">react's changelog</a>.</em></p>
<blockquote>
<h2>19.2.7 (June 1, 2026)</h2>
<h3>React Server Components</h3>
<ul>
<li>Fixed missing <code>FormData</code> entries in Server Actions which regressed in 19.2.6 (<a href="https://github.com/unstubbable"><code>@​unstubbable</code></a> <a href="https://redirect.github.com/facebook/react/pull/36566">#36566</a>)</li>
</ul>
</blockquote>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/react/react/commit/1dd4ecbdabf826f527fc9a58c05ea70375b7d170"><code>1dd4ecb</code></a> [FlightReply] Performance improvements when decoding (<a href="https://github.com/react/react/tree/HEAD/packages/react/issues/37087">#37087</a>)</li>
<li><a href="https://github.com/react/react/commit/b0d2fdb78bdfae075a7fa02ddcebbf25f90952c2"><code>b0d2fdb</code></a> [19.2.x] Update required references to GitHub repo (<a href="https://github.com/react/react/tree/HEAD/packages/react/issues/36753">#36753</a>)</li>
<li><a href="https://github.com/react/react/commit/6117d7cca4906492c51fe6a03381e35adfd86e7d"><code>6117d7c</code></a> Version 19.2.7 (<a href="https://github.com/react/react/tree/HEAD/packages/react/issues/36591">#36591</a>)</li>
<li>See full diff in <a href="https://github.com/react/react/commits/v19.2.8/packages/react">compare view</a></li>
</ul>
</details>
<details>
<summary>Maintainer changes</summary>
<p>This version was pushed to npm by <a href="https://www.npmjs.com/~GitHub%20Actions">GitHub Actions</a>, a new releaser for react since your current version.</p>
</details>
<br />

Updates `@types/react` from 19.2.14 to 19.2.18
<details>
<summary>Commits</summary>
<ul>
<li>See full diff in <a href="https://github.com/DefinitelyTyped/DefinitelyTyped/commits/HEAD/types/react">compare view</a></li>
</ul>
</details>
<br />

Updates `react-dom` from 19.2.6 to 19.2.8
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/react/react/releases">react-dom's releases</a>.</em></p>
<blockquote>
<h2>19.2.8 (July 21st, 2026)</h2>
<h2>React Server Components</h2>
<ul>
<li>Performance improvements when decoding
(<a href="https://redirect.github.com/facebook/react/pull/37087">#37087</a> by <a href="https://github.com/eps1lon"><code>@​eps1lon</code></a>)</li>
</ul>
<h2>19.2.7 (June 1st, 2026)</h2>
<h2>React Server Components</h2>
<ul>
<li>Fixed missing <code>FormData</code> entries in Server Actions which regressed in 19.2.6
(<a href="https://redirect.github.com/facebook/react/pull/36566">#36566</a> by <a href="https://github.com/unstubbable"><code>@​unstubbable</code></a>)</li>
</ul>
</blockquote>
</details>
<details>
<summary>Changelog</summary>
<p><em>Sourced from <a href="https://github.com/react/react/blob/main/CHANGELOG.md">react-dom's changelog</a>.</em></p>
<blockquote>
<h2>19.2.7 (June 1, 2026)</h2>
<h3>React Server Components</h3>
<ul>
<li>Fixed missing <code>FormData</code> entries in Server Actions which regressed in 19.2.6 (<a href="https://github.com/unstubbable"><code>@​unstubbable</code></a> <a href="https://redirect.github.com/facebook/react/pull/36566">#36566</a>)</li>
</ul>
</blockquote>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/react/react/commit/1dd4ecbdabf826f527fc9a58c05ea70375b7d170"><code>1dd4ecb</code></a> [FlightReply] Performance improvements when decoding (<a href="https://github.com/react/react/tree/HEAD/packages/react-dom/issues/37087">#37087</a>)</li>
<li><a href="https://github.com/react/react/commit/b0d2fdb78bdfae075a7fa02ddcebbf25f90952c2"><code>b0d2fdb</code></a> [19.2.x] Update required references to GitHub repo (<a href="https://github.com/react/react/tree/HEAD/packages/react-dom/issues/36753">#36753</a>)</li>
<li><a href="https://github.com/react/react/commit/6117d7cca4906492c51fe6a03381e35adfd86e7d"><code>6117d7c</code></a> Version 19.2.7 (<a href="https://github.com/react/react/tree/HEAD/packages/react-dom/issues/36591">#36591</a>)</li>
<li>See full diff in <a href="https://github.com/react/react/commits/v19.2.8/packages/react-dom">compare view</a></li>
</ul>
</details>
<details>
<summary>Maintainer changes</summary>
<p>This version was pushed to npm by <a href="https://www.npmjs.com/~GitHub%20Actions">GitHub Actions</a>, a new releaser for react-dom since your current version.</p>
</details>
<br />

Updates `@types/react-dom` from 19.2.3 to 19.2.5
<details>
<summary>Commits</summary>
<ul>
<li>See full diff in <a href="https://github.com/DefinitelyTyped/DefinitelyTyped/commits/HEAD/types/react-dom">compare view</a></li>
</ul>
</details>
<br />

Updates `yjs` from 13.6.30 to 13.6.32
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/yjs/yjs/releases">yjs's releases</a>.</em></p>
<blockquote>
<h2>v13.6.32</h2>
<ul>
<li>fix <a href="https://redirect.github.com/yjs/yjs/issues/797">#797</a> - undomanager clears destroy handler  95e890d9</li>
</ul>
<hr />
<p><a href="https://github.com/yjs/yjs/compare/v13.6.31...v13.6.32">https://github.com/yjs/yjs/compare/v13.6.31...v13.6.32</a></p>
<h2>v13.6.31</h2>
<ul>
<li>Merge branch &amp;<a href="https://redirect.github.com/yjs/yjs/issues/39">#39</a>;ppiotrowicz-fix/757-undo-attr-redo&amp;<a href="https://redirect.github.com/yjs/yjs/issues/39">#39</a>; into v13  1ddba7e4</li>
<li>fix <a href="https://redirect.github.com/yjs/yjs/issues/757">#757</a> in v13  d9aaff72</li>
<li>fix undoing setAttribute combined with delete corrupts remote state - closes <a href="https://redirect.github.com/yjs/yjs/issues/757">#757</a>  67c809ee</li>
</ul>
<hr />
<p><a href="https://github.com/yjs/yjs/compare/v13.6.30...v13.6.31">https://github.com/yjs/yjs/compare/v13.6.30...v13.6.31</a></p>
</blockquote>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/yjs/yjs/commit/1ce38f75f786e4bc0b2cc9703afbc6eea8fe7859"><code>1ce38f7</code></a> 13.6.32</li>
<li><a href="https://github.com/yjs/yjs/commit/95e890d99ac6b8462fc02722e60b1dbd17c9c29d"><code>95e890d</code></a> fix <a href="https://redirect.github.com/yjs/yjs/issues/797">#797</a> - undomanager clears destroy handler</li>
<li><a href="https://github.com/yjs/yjs/commit/271330889b13eae102873bb417d6747a0ddd8b4a"><code>2713308</code></a> 13.6.31</li>
<li><a href="https://github.com/yjs/yjs/commit/1ddba7e48cfa9cdf4c0c51b2a1bd22986a0e8704"><code>1ddba7e</code></a> Merge branch 'ppiotrowicz-fix/757-undo-attr-redo' into v13</li>
<li><a href="https://github.com/yjs/yjs/commit/d9aaff72b246c0f2a5c07eaa4f685079fe9e6e5a"><code>d9aaff7</code></a> fix <a href="https://redirect.github.com/yjs/yjs/issues/757">#757</a> in v13</li>
<li><a href="https://github.com/yjs/yjs/commit/67c809ee6b787984d7bf709df9900b93cccffb7e"><code>67c809e</code></a> fix undoing setAttribute combined with delete corrupts remote state - closes ...</li>
<li>See full diff in <a href="https://github.com/yjs/yjs/compare/v13.6.30...v13.6.32">compare view</a></li>
</ul>
</details>
<br />

Updates `@types/pg` from 8.20.0 to 8.23.1
<details>
<summary>Commits</summary>
<ul>
<li>See full diff in <a href="https://github.com/DefinitelyTyped/DefinitelyTyped/commits/HEAD/types/pg">compare view</a></li>
</ul>
</details>
<br />

Updates `@types/react` from 19.2.14 to 19.2.18
<details>
<summary>Commits</summary>
<ul>
<li>See full diff in <a href="https://github.com/DefinitelyTyped/DefinitelyTyped/commits/HEAD/types/react">compare view</a></li>
</ul>
</details>
<br />

Updates `@types/react-dom` from 19.2.3 to 19.2.5
<details>
<summary>Commits</summary>
<ul>
<li>See full diff in <a href="https://github.com/DefinitelyTyped/DefinitelyTyped/commits/HEAD/types/react-dom">compare view</a></li>
</ul>
</details>
<br />

<details><summary>Comment — nathanpond, 2026-08-31</summary>

**Held** (2026-08-31): `tsc` passes for the sidecar with these bumps, but `@blocknote/core`/`@blocknote/server-util` 0.54 must match the SPA's BlockNote version (Yjs document format), and the SPA can't take 0.54 yet — see archived-107. `@hocuspocus/server` 4.6 likewise pairs with `@hocuspocus/provider` 4.6 in the SPA. Land together with the SPA side.

</details>

<details><summary>Comment — dependabot[bot], 2026-08-31</summary>

Looks like these dependencies are updatable in another way, so this is no longer needed.

</details>

---

## archived-105 — Bump @types/node from 24.12.4 to 26.4.0 in /services/hocuspocus

`CLOSED` · app/dependabot · opened 2026-08-31 · `dependabot/npm_and_yarn/services/hocuspocus/types/node-26.4.0` → `master`

Bumps [@types/node](https://github.com/DefinitelyTyped/DefinitelyTyped/tree/HEAD/types/node) from 24.12.4 to 26.4.0.
<details>
<summary>Commits</summary>
<ul>
<li>See full diff in <a href="https://github.com/DefinitelyTyped/DefinitelyTyped/commits/HEAD/types/node">compare view</a></li>
</ul>
</details>
<br />


[![Dependabot compatibility score](https://dependabot-badges.githubapp.com/badges/compatibility_score?dependency-name=@types/node&package-manager=npm_and_yarn&previous-version=24.12.4&new-version=26.4.0)](https://docs.github.com/en/github/managing-security-vulnerabilities/about-dependabot-security-updates#about-compatibility-scores)

Dependabot will resolve any conflicts with this PR as long as you don't alter it yourself. You can also trigger a rebase manually by commenting `@dependabot rebase`.

[//]: # (dependabot-automerge-start)
[//]: # (dependabot-automerge-end)

---

<details>
<summary>Dependabot commands and options</summary>
<br />

You can trigger Dependabot actions by commenting on this PR:
- `@dependabot rebase` will rebase this PR
- `@dependabot recreate` will recreate this PR, overwriting any edits that have been made to it
- `@dependabot show <dependency name> ignore conditions` will show all of the ignore conditions of the specified dependency
- `@dependabot ignore this major version` will close this PR and stop Dependabot creating any more for this major version (unless you reopen the PR or upgrade to it yourself)
- `@dependabot ignore this minor version` will close this PR and stop Dependabot creating any more for this minor version (unless you reopen the PR or upgrade to it yourself)
- `@dependabot ignore this dependency` will close this PR and stop Dependabot creating any more for this dependency (unless you reopen the PR or upgrade to it yourself)


</details>

<details><summary>Comment — nathanpond, 2026-08-31</summary>

**Held** (2026-08-31): `@types/node` should track the runtime. Both sidecar images are `node:22-alpine` (`services/*/Dockerfile`); bump this when the base image moves to Node 26.

</details>

<details><summary>Comment — nathanpond, 2026-08-31</summary>

Closing rather than merging: `@types/node` should track the runtime major, and the runtime was just standardised on **Node 24** (archived-139 / archived-140) — the 24.x types already in the lockfile are the correct ones. This bump will be wanted again as a 24 → 26 move once Node 26 enters LTS (October 2026); Dependabot now also tracks the Docker base images so both arrive together.

</details>

<details><summary>Comment — dependabot[bot], 2026-08-31</summary>

OK, I won't notify you again about this release, but will get in touch when a new version is available. If you'd rather skip all updates until the next major or minor version, let me know by commenting `@dependabot ignore this major version` or `@dependabot ignore this minor version`. You can also ignore all major, minor, or patch releases for a dependency by adding an [`ignore` condition](https://docs.github.com/en/code-security/supply-chain-security/configuration-options-for-dependency-updates#ignore) with the desired `update_types` to your config file.

If you change your mind, just re-open this PR and I'll resolve any conflicts on it.

</details>

---

## archived-106 — chore(deps-dev): bump typescript from 5.9.3 to 7.0.2 in /services/hocuspocus

`MERGED (merged 2026-08-31)` · app/dependabot · opened 2026-08-31 · `dependabot/npm_and_yarn/services/hocuspocus/typescript-7.0.2` → `master`

Bumps [typescript](https://github.com/microsoft/TypeScript) from 5.9.3 to 7.0.2.
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/microsoft/TypeScript/releases">typescript's releases</a>.</em></p>
<blockquote>
<h2>TypeScript 7.0.2</h2>
<p><a href="https://devblogs.microsoft.com/typescript/announcing-typescript-7-0/">https://devblogs.microsoft.com/typescript/announcing-typescript-7-0/</a></p>
<p>This tag was originally released at: <a href="https://github.com/microsoft/typescript-go/releases/tag/typescript%2Fv7.0.2">https://github.com/microsoft/typescript-go/releases/tag/typescript%2Fv7.0.2</a></p>
<h2>TypeScript 6.0.3</h2>
<p>For release notes, check out the <a href="https://devblogs.microsoft.com/typescript/announcing-typescript-6-0/">release announcement blog post</a>.</p>
<ul>
<li><a href="https://github.com/Microsoft/TypeScript/issues?utf8=%E2%9C%93&amp;q=milestone%3A%22TypeScript+6.0.0%22">fixed issues query for TypeScript 6.0.0 (Beta)</a>.</li>
<li><a href="https://github.com/Microsoft/TypeScript/issues?utf8=%E2%9C%93&amp;q=milestone%3A%22TypeScript+6.0.1%22">fixed issues query for TypeScript 6.0.1 (RC)</a>.</li>
<li><a href="https://github.com/Microsoft/TypeScript/issues?utf8=%E2%9C%93&amp;q=milestone%3A%22TypeScript+6.0.2%22">fixed issues query for TypeScript 6.0.2 (Stable)</a>.</li>
<li><a href="https://github.com/Microsoft/TypeScript/issues?utf8=%E2%9C%93&amp;q=milestone%3A%22TypeScript+6.0.3%22">fixed issues query for TypeScript 6.0.3 (Stable)</a>.</li>
</ul>
<p>Downloads are available on:</p>
<ul>
<li><a href="https://www.npmjs.com/package/typescript">npm</a></li>
</ul>
<h2>TypeScript 6.0</h2>
<p>For release notes, check out the <a href="https://devblogs.microsoft.com/typescript/announcing-typescript-6-0/">release announcement blog post</a>.</p>
<ul>
<li><a href="https://github.com/Microsoft/TypeScript/issues?utf8=%E2%9C%93&amp;q=milestone%3A%22TypeScript+6.0.0%22">fixed issues query for TypeScript 6.0.0 (Beta)</a>.</li>
<li><a href="https://github.com/Microsoft/TypeScript/issues?utf8=%E2%9C%93&amp;q=milestone%3A%22TypeScript+6.0.1%22">fixed issues query for TypeScript 6.0.1 (RC)</a>.</li>
<li><a href="https://github.com/Microsoft/TypeScript/issues?utf8=%E2%9C%93&amp;q=milestone%3A%22TypeScript+6.0.2%22">fixed issues query for TypeScript 6.0.2 (Stable)</a>.</li>
</ul>
<p>Downloads are available on:</p>
<ul>
<li><a href="https://www.npmjs.com/package/typescript">npm</a></li>
</ul>
<h2>TypeScript 6.0.1 RC</h2>
<p>For release notes, check out the <a href="https://devblogs.microsoft.com/typescript/announcing-typescript-6-0-rc/">release announcement blog post</a>.</p>
<ul>
<li><a href="https://github.com/Microsoft/TypeScript/issues?utf8=%E2%9C%93&amp;q=milestone%3A%22TypeScript+6.0.0%22">fixed issues query for TypeScript 6.0.0 (Beta)</a>.</li>
<li><a href="https://github.com/Microsoft/TypeScript/issues?utf8=%E2%9C%93&amp;q=milestone%3A%22TypeScript+6.0.1%22">fixed issues query for TypeScript 6.0.1 (RC)</a>.</li>
</ul>
<p>Downloads are available on:</p>
<ul>
<li><a href="https://www.npmjs.com/package/typescript">npm</a></li>
</ul>
<h2>TypeScript 6.0 Beta</h2>
<p>For release notes, check out the <a href="https://devblogs.microsoft.com/typescript/announcing-typescript-6-0-beta/">release announcement</a>.</p>
<ul>
<li><a href="https://github.com/Microsoft/TypeScript/issues?utf8=%E2%9C%93&amp;q=milestone%3A%22TypeScript+6.0.0%22+is%3Aclosed+">fixed issues query for Typescript 6.0.0 (Beta)</a>.</li>
</ul>
<p>Downloads are available on:</p>
<ul>
<li><a href="https://www.npmjs.com/package/typescript">npm</a></li>
</ul>
</blockquote>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/microsoft/TypeScript/commit/1e4744d68260a7cb91b62b12edc3f6a2187faaf1"><code>1e4744d</code></a> Merge branch 'main' into ts7-release</li>
<li><a href="https://github.com/microsoft/TypeScript/commit/a5a219c3b5da0db4fa0ecf6c0b1f588c9af9c669"><code>a5a219c</code></a><code>microsoft/typescript-go#4558</code></li>
<li><a href="https://github.com/microsoft/TypeScript/commit/ecfe30dce91368d52c9a49b6095bb0b673a238f8"><code>ecfe30d</code></a> Update status localization</li>
<li><a href="https://github.com/microsoft/TypeScript/commit/5de25b5f8fec2ca35eadaed041f1f06d2e214895"><code>5de25b5</code></a> Hide executable name in TypeScript status</li>
<li><a href="https://github.com/microsoft/TypeScript/commit/d7ce74a75da2b80e8201506a1599c06549432b93"><code>d7ce74a</code></a> Show bundled TypeScript version for packaged servers</li>
<li><a href="https://github.com/microsoft/TypeScript/commit/29be66a607707f90d7a53103a4469bb3015a4d54"><code>29be66a</code></a> Correct TS 7 release version to 7.0.2</li>
<li><a href="https://github.com/microsoft/TypeScript/commit/ed2bd1bfa4aac5211ce4bc58fcd1313c7eddc8ff"><code>ed2bd1b</code></a> Merge branch 'main' into ts7-release</li>
<li><a href="https://github.com/microsoft/TypeScript/commit/887307575c58ea640dbeba3b4e8fdb6347cd3044"><code>8873075</code></a> Bump the github-actions group across 1 directory with 3 updates (microsoft/ty...</li>
<li><a href="https://github.com/microsoft/TypeScript/commit/9427131ae2d4e230a90ee8a09daac4e75da3e311"><code>9427131</code></a> Set up stable / nightly extension split, other prep (microsoft/typescript-go#...</li>
<li><a href="https://github.com/microsoft/TypeScript/commit/d4eaca5460a1f5f02a829e62706794b0a6fb903e"><code>d4eaca5</code></a><code>microsoft/typescript-go#4549</code></li>
<li>Additional commits viewable in <a href="https://github.com/microsoft/TypeScript/compare/v5.9.3...v7.0.2">compare view</a></li>
</ul>
</details>
<details>
<summary>Maintainer changes</summary>
<p>This version was pushed to npm by <a href="https://www.npmjs.com/~microsoft1es">microsoft1es</a>, a new releaser for typescript since your current version.</p>
</details>
<br />

<details><summary>Comment — nathanpond, 2026-08-31</summary>

**Held** (2026-08-31): TypeScript 7 is the new native (Go) compiler — a major toolchain change. Not adopting it via a Dependabot bump; needs a deliberate upgrade of `typescript-eslint`, Vite plugin and build scripts together, verified across the SPA and both sidecars.

</details>

<details><summary>Comment — nathanpond, 2026-08-31</summary>

@dependabot rebase

</details>

<details><summary>Comment — nathanpond, 2026-08-31</summary>

Validated on the rebased head (`758ac63`), which now sits on the Hocuspocus 4.6 lockfile:

- `npm ci` clean, `npx tsc --version` → 7.0.2
- `npm run build` → clean compile, `dist/` emitted

`services/hocuspocus` needed **no** tsconfig change for TypeScript 7 — it already sets `rootDir`, so it compiles as-is. (The executor did need one; that landed separately in archived-170 before archived-99.)

Like the executor, this sidecar has no ESLint, no bundler and no Vite plugin — its whole toolchain is `tsc` — so the "upgrade typescript-eslint and the Vite plugin together" hold applied to archived-109 (the SPA), not here.

The SPA hold stands: `typescript-eslint@8.69.0` declares `typescript: ">=4.8.4 <6.1.0"`, so TS 7 is out of range there until upstream ships support.

</details>

---

## archived-107 — Bump the spa-minor-patch group across 1 directory with 40 updates

`CLOSED` · app/dependabot · opened 2026-08-31 · `dependabot/npm_and_yarn/src/AutoNate.Spa/spa-minor-patch-f83b28ffca` → `master`

Bumps the spa-minor-patch group with 40 updates in the /src/AutoNate.Spa directory:

| Package | From | To |
| --- | --- | --- |
| [@blocknote/core](https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/core) | `0.51.0` | `0.54.0` |
| [@blocknote/mantine](https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/mantine) | `0.51.0` | `0.54.0` |
| [@blocknote/react](https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/react) | `0.51.0` | `0.54.0` |
| [@codemirror/lang-html](https://github.com/codemirror/lang-html) | `6.4.11` | `6.4.12` |
| [@eigenpal/docx-editor-agents](https://github.com/eigenpal/docx-editor/tree/HEAD/packages/agents) | `1.0.3` | `1.9.0` |
| [@eigenpal/docx-editor-core](https://github.com/eigenpal/docx-editor/tree/HEAD/packages/core) | `1.0.3` | `1.9.0` |
| [@eigenpal/docx-editor-i18n](https://github.com/eigenpal/docx-editor/tree/HEAD/packages/i18n) | `1.0.3` | `1.9.0` |
| [@eigenpal/docx-editor-react](https://github.com/eigenpal/docx-editor/tree/HEAD/packages/react) | `1.0.3` | `1.9.0` |
| [@fortawesome/fontawesome-free](https://github.com/FortAwesome/Font-Awesome) | `7.2.0` | `7.3.1` |
| [@hocuspocus/provider](https://github.com/ueberdosis/hocuspocus) | `4.0.0` | `4.6.0` |
| [@mantine/charts](https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts) | `9.1.1` | `9.5.2` |
| [@mantine/colors-generator](https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator) | `9.1.1` | `9.5.2` |
| [@mantine/core](https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/core) | `9.1.1` | `9.5.2` |
| [@mantine/dates](https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dates) | `9.1.1` | `9.5.2` |
| [@mantine/dropzone](https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dropzone) | `9.1.1` | `9.5.2` |
| [@mantine/form](https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/form) | `9.1.1` | `9.5.2` |
| [@mantine/hooks](https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/hooks) | `9.1.1` | `9.5.2` |
| [@mantine/modals](https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/modals) | `9.1.1` | `9.5.2` |
| [@mantine/notifications](https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/notifications) | `9.1.1` | `9.5.2` |
| [@tanstack/react-query](https://github.com/TanStack/query/tree/HEAD/packages/react-query) | `5.100.1` | `5.102.8` |
| [@tanstack/react-query-devtools](https://github.com/TanStack/query/tree/HEAD/packages/react-query-devtools) | `5.100.1` | `5.102.8` |
| [@uiw/react-codemirror](https://github.com/uiwjs/react-codemirror) | `4.25.9` | `4.25.11` |
| [@xyflow/react](https://github.com/xyflow/xyflow/tree/HEAD/packages/react) | `12.10.2` | `12.11.5` |
| [axios](https://github.com/axios/axios) | `1.15.2` | `1.20.0` |
| [dayjs](https://github.com/iamkun/dayjs) | `1.11.20` | `1.11.23` |
| [marked](https://github.com/markedjs/marked) | `18.0.4` | `18.0.11` |
| [react](https://github.com/react/react/tree/HEAD/packages/react) | `19.2.5` | `19.2.8` |
| [@types/react](https://github.com/DefinitelyTyped/DefinitelyTyped/tree/HEAD/types/react) | `19.2.14` | `19.2.18` |
| [react-dom](https://github.com/react/react/tree/HEAD/packages/react-dom) | `19.2.5` | `19.2.8` |
| [@types/react-dom](https://github.com/DefinitelyTyped/DefinitelyTyped/tree/HEAD/types/react-dom) | `19.2.3` | `19.2.5` |
| [react-grid-layout](https://github.com/STRML/react-grid-layout) | `2.2.3` | `2.2.4` |
| [@types/react-grid-layout](https://github.com/DefinitelyTyped/DefinitelyTyped/tree/HEAD/types/react-grid-layout) | `1.3.6` | `2.1.0` |
| [react-router-dom](https://github.com/remix-run/react-router/tree/HEAD/packages/react-router-dom) | `7.14.2` | `7.18.2` |
| [recharts](https://github.com/recharts/recharts) | `3.8.1` | `3.10.1` |
| [yjs](https://github.com/yjs/yjs) | `13.6.30` | `13.6.32` |
| [zod](https://github.com/colinhacks/zod) | `4.3.6` | `4.4.3` |
| [@vitejs/plugin-react](https://github.com/vitejs/vite-plugin-react/tree/HEAD/packages/plugin-react) | `6.0.1` | `6.1.1` |
| [globals](https://github.com/sindresorhus/globals) | `17.6.0` | `17.11.0` |
| [typescript-eslint](https://github.com/typescript-eslint/typescript-eslint/tree/HEAD/packages/typescript-eslint) | `8.60.0` | `8.68.0` |
| [vite](https://github.com/vitejs/vite/tree/HEAD/packages/vite) | `8.0.10` | `8.2.2` |


Updates `@blocknote/core` from 0.51.0 to 0.54.0
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/TypeCellOS/BlockNote/releases">@​blocknote/core's releases</a>.</em></p>
<blockquote>
<h2>v0.54.0</h2>
<h2>0.54.0 (2026-08-13)</h2>
<p>💖 The math block and diagram block has been sponsored by <a href="https://www.numerique.gouv.fr/dinum/">DINUM</a> 🇫🇷</p>
<h3>Math Block</h3>
<p>A long requested feature, you can now add block &amp; inline math to a BlockNote editor. They are driven by <a href="https://katex.org/">Katex</a> &amp; support much of <a href="https://www.latex-project.org/">Latex</a> for all your notation needs.</p>
<p><a href="https://github.com/user-attachments/assets/8fb5790e-6922-4f02-a35f-27c791b877e8">https://github.com/user-attachments/assets/8fb5790e-6922-4f02-a35f-27c791b877e8</a></p>
<p><a href="https://www.blocknotejs.org/examples/custom-schema/math-block">Link to demo</a></p>
<h3>Diagram Block</h3>
<p>We've also added support for a diagram block driven by <a href="https://mermaid.js.org/">Mermaid.js</a>, allowing you to add diagramming to the editor.</p>
<p><a href="https://github.com/user-attachments/assets/0a64e98a-5bf0-4dec-b1a4-84ccf98f4a70">https://github.com/user-attachments/assets/0a64e98a-5bf0-4dec-b1a4-84ccf98f4a70</a></p>
<p><a href="https://www.blocknotejs.org/examples/custom-schema/diagram-block">Link to demo</a></p>
<h3>Source Block with Preview</h3>
<p>Both the Math block &amp; Diagram block are built on a primitive that you can build your own custom blocks from. The Source Block with Preview primitive allows you to build a pair of a block which renders content with an inline editor for the content being rendered. This can enable other sorts of preview-like features in the future, exposed as an API for you to build your own custom blocks with.</p>
<!-- raw HTML omitted -->
<!-- raw HTML omitted -->
<p><a href="https://www.blocknotejs.org/examples/custom-schema/source-with-preview">Link to demo</a></p>
<h3>🚀 Features</h3>
<ul>
<li>Adds a Math block (<a href="https://github.com/TypeCellOS/BlockNote/commit/2a34f7d70">2a34f7d70</a>)</li>
<li>Adds a Diagram block (<a href="https://github.com/TypeCellOS/BlockNote/commit/0fca0ee7a">0fca0ee7a</a>)</li>
<li><strong>core:</strong> Source-with-preview, syntax highlighting &amp; exporter images (<a href="https://github.com/TypeCellOS/BlockNote/commit/503c796d3">503c796d3</a>)</li>
</ul>
<h3>🩹 Fixes</h3>
<ul>
<li><strong>ai:</strong> Operations on collaborative documents (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2952">#2952</a>)</li>
<li><strong>ai:</strong> Operations on blocks containing comments (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2953">#2953</a>)</li>
<li><strong>pdf:</strong> Add custom font and fontFamily options for CJK (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2945">#2945</a>)</li>
<li>Expose first suggestion as active descendant (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2965">#2965</a>)</li>
<li><strong>xl-docx-exporter:</strong> Clamp list nesting to the levels DOCX defines (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2969">#2969</a>)</li>
</ul>
<h3>❤️ Thank You</h3>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Changelog</summary>
<p><em>Sourced from <a href="https://github.com/TypeCellOS/BlockNote/blob/main/CHANGELOG.md">@​blocknote/core's changelog</a>.</em></p>
<blockquote>
<h2>0.54.0 (2026-08-13)</h2>
<h3>🚀 Features</h3>
<ul>
<li>Adds a Math block (<a href="https://github.com/TypeCellOS/BlockNote/commit/2a34f7d70">2a34f7d70</a>)</li>
<li>Adds a Diagram block (<a href="https://github.com/TypeCellOS/BlockNote/commit/0fca0ee7a">0fca0ee7a</a>)</li>
<li><strong>core:</strong> Source-with-preview, syntax highlighting &amp; exporter images (<a href="https://github.com/TypeCellOS/BlockNote/commit/503c796d3">503c796d3</a>)</li>
</ul>
<h3>🩹 Fixes</h3>
<ul>
<li><strong>ai:</strong> Operations on collaborative documents (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2952">#2952</a>)</li>
<li><strong>ai:</strong> Operations on blocks containing comments (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2953">#2953</a>)</li>
<li><strong>pdf:</strong> Add custom font and fontFamily options for CJK (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2945">#2945</a>)</li>
<li>Expose first suggestion as active descendant (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2965">#2965</a>)</li>
<li><strong>xl-docx-exporter:</strong> Clamp list nesting to the levels DOCX defines (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2969">#2969</a>)</li>
</ul>
<h3>❤️ Thank You</h3>
<ul>
<li>Adarshsm <a href="mailto:adarshmudugal@gmail.com">adarshmudugal@gmail.com</a></li>
<li>Nick The Sick (<a href="https://github.com/nperez0111"><code>@​nperez0111</code></a>)</li>
<li>Pupuking723 <a href="mailto:2318857637@qq.com">2318857637@qq.com</a></li>
</ul>
<h2>0.53.0 (2026-08-06)</h2>
<h3>🚀 Features</h3>
<ul>
<li><strong>shadcn:</strong> ⚠️ Use base-ui instead of radix (BLO-1279) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2913">#2913</a>)</li>
</ul>
<h3>🩹 Fixes</h3>
<ul>
<li>getCellSelection throwing error in positions (BLO-1193) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2911">#2911</a>)</li>
<li>Multi-column slash menu items within a column (BLO-905) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2914">#2914</a>)</li>
<li>Suggestion menu behaviour (BLO-1283, BLO-955) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2930">#2930</a>)</li>
<li>Ignore useless block/inline content mutations (BLO-1224) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2912">#2912</a>)</li>
<li><strong>slash-menu:</strong> Better overflow behavior (BLO-1192) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2909">#2909</a>)</li>
<li>Slash menu item selection behaviour (BLO-1222) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2838">#2838</a>)</li>
<li>HTML export/parse round trip ignoring empty blocks (BLO-873) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2931">#2931</a>)</li>
<li><strong>core:</strong> Guard getBlock() calls to prevent TypeError on stale blocks (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2941">#2941</a>)</li>
<li>Stop stale node view positions crashing the editor (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2938">#2938</a>)</li>
<li>Multi-column trailing blocks, column hover borders &amp; drop cursor left edge BLO-1226 (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2885">#2885</a>)</li>
</ul>
<h4>⚠️ Breaking Changes</h4>
<ul>
<li><strong>shadcn:</strong> ⚠️ Use base-ui instead of radix (BLO-1279) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2913">#2913</a>)</li>
</ul>
<h3>❤️ Thank You</h3>
<ul>
<li>Yousef</li>
<li>Nick Perez <a href="mailto:nick@blocknotejs.org">nick@blocknotejs.org</a></li>
<li>Matthew Lipski (<a href="https://github.com/matthewlipski"><code>@​matthewlipski</code></a>)</li>
</ul>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/ea5d80358f179d1683abcd2e0e3e9d547bf52eef"><code>ea5d803</code></a> chore(release): v0.54.0</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/503c796d37f2c8734cf65e9bad3348127043c63b"><code>503c796</code></a> feat(core): source-with-preview, syntax highlighting &amp; exporter images</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/99253c3814a93e6f5d1ae318efeb0b10df90f32d"><code>99253c3</code></a> chore: migrate to TypeScript 7 and consolidate the <a href="https://github.com/shared"><code>@​shared</code></a> alias</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/bea469e31eab19242b1238cd3600a14c1d6148c1"><code>bea469e</code></a> refactor: vendor <code>@​tanstack/store</code> as a first-party Store (<a href="https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/core/issues/2956">#2956</a>)</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/dee3401a2647eb01b7a982b32e98e0bd182713fe"><code>dee3401</code></a> chore: bump prosemirror-view to ^1.42.2 (<a href="https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/core/issues/2954">#2954</a>)</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/decb3d21480ceed983d3befb4e87ff8d26bcc938"><code>decb3d2</code></a> fix(ai): operations on blocks containing comments (<a href="https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/core/issues/2953">#2953</a>)</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/824abce757ed1a44e4dbb048fe88ea954b592831"><code>824abce</code></a> fix(ai): operations on collaborative documents (<a href="https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/core/issues/2952">#2952</a>)</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/529c3b02f6e413c362e96718dd712dd4b4c495a0"><code>529c3b0</code></a> chore(release): v0.53.0</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/d998f0168abd54ec57239479ea2dfc3d17df6a1a"><code>d998f01</code></a> fix: multi-column trailing blocks, column hover borders &amp; drop cursor left ed...</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/58d43ff08806ce078f03cf5a28afeefb1bede482"><code>58d43ff</code></a> fix: stop stale node view positions crashing the editor (<a href="https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/core/issues/2938">#2938</a>)</li>
<li>Additional commits viewable in <a href="https://github.com/TypeCellOS/BlockNote/commits/v0.54.0/packages/core">compare view</a></li>
</ul>
</details>
<br />

Updates `@blocknote/mantine` from 0.51.0 to 0.54.0
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/TypeCellOS/BlockNote/releases">@​blocknote/mantine's releases</a>.</em></p>
<blockquote>
<h2>v0.54.0</h2>
<h2>0.54.0 (2026-08-13)</h2>
<p>💖 The math block and diagram block has been sponsored by <a href="https://www.numerique.gouv.fr/dinum/">DINUM</a> 🇫🇷</p>
<h3>Math Block</h3>
<p>A long requested feature, you can now add block &amp; inline math to a BlockNote editor. They are driven by <a href="https://katex.org/">Katex</a> &amp; support much of <a href="https://www.latex-project.org/">Latex</a> for all your notation needs.</p>
<p><a href="https://github.com/user-attachments/assets/8fb5790e-6922-4f02-a35f-27c791b877e8">https://github.com/user-attachments/assets/8fb5790e-6922-4f02-a35f-27c791b877e8</a></p>
<p><a href="https://www.blocknotejs.org/examples/custom-schema/math-block">Link to demo</a></p>
<h3>Diagram Block</h3>
<p>We've also added support for a diagram block driven by <a href="https://mermaid.js.org/">Mermaid.js</a>, allowing you to add diagramming to the editor.</p>
<p><a href="https://github.com/user-attachments/assets/0a64e98a-5bf0-4dec-b1a4-84ccf98f4a70">https://github.com/user-attachments/assets/0a64e98a-5bf0-4dec-b1a4-84ccf98f4a70</a></p>
<p><a href="https://www.blocknotejs.org/examples/custom-schema/diagram-block">Link to demo</a></p>
<h3>Source Block with Preview</h3>
<p>Both the Math block &amp; Diagram block are built on a primitive that you can build your own custom blocks from. The Source Block with Preview primitive allows you to build a pair of a block which renders content with an inline editor for the content being rendered. This can enable other sorts of preview-like features in the future, exposed as an API for you to build your own custom blocks with.</p>
<!-- raw HTML omitted -->
<!-- raw HTML omitted -->
<p><a href="https://www.blocknotejs.org/examples/custom-schema/source-with-preview">Link to demo</a></p>
<h3>🚀 Features</h3>
<ul>
<li>Adds a Math block (<a href="https://github.com/TypeCellOS/BlockNote/commit/2a34f7d70">2a34f7d70</a>)</li>
<li>Adds a Diagram block (<a href="https://github.com/TypeCellOS/BlockNote/commit/0fca0ee7a">0fca0ee7a</a>)</li>
<li><strong>core:</strong> Source-with-preview, syntax highlighting &amp; exporter images (<a href="https://github.com/TypeCellOS/BlockNote/commit/503c796d3">503c796d3</a>)</li>
</ul>
<h3>🩹 Fixes</h3>
<ul>
<li><strong>ai:</strong> Operations on collaborative documents (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2952">#2952</a>)</li>
<li><strong>ai:</strong> Operations on blocks containing comments (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2953">#2953</a>)</li>
<li><strong>pdf:</strong> Add custom font and fontFamily options for CJK (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2945">#2945</a>)</li>
<li>Expose first suggestion as active descendant (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2965">#2965</a>)</li>
<li><strong>xl-docx-exporter:</strong> Clamp list nesting to the levels DOCX defines (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2969">#2969</a>)</li>
</ul>
<h3>❤️ Thank You</h3>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Changelog</summary>
<p><em>Sourced from <a href="https://github.com/TypeCellOS/BlockNote/blob/main/CHANGELOG.md">@​blocknote/mantine's changelog</a>.</em></p>
<blockquote>
<h2>0.54.0 (2026-08-13)</h2>
<h3>🚀 Features</h3>
<ul>
<li>Adds a Math block (<a href="https://github.com/TypeCellOS/BlockNote/commit/2a34f7d70">2a34f7d70</a>)</li>
<li>Adds a Diagram block (<a href="https://github.com/TypeCellOS/BlockNote/commit/0fca0ee7a">0fca0ee7a</a>)</li>
<li><strong>core:</strong> Source-with-preview, syntax highlighting &amp; exporter images (<a href="https://github.com/TypeCellOS/BlockNote/commit/503c796d3">503c796d3</a>)</li>
</ul>
<h3>🩹 Fixes</h3>
<ul>
<li><strong>ai:</strong> Operations on collaborative documents (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2952">#2952</a>)</li>
<li><strong>ai:</strong> Operations on blocks containing comments (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2953">#2953</a>)</li>
<li><strong>pdf:</strong> Add custom font and fontFamily options for CJK (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2945">#2945</a>)</li>
<li>Expose first suggestion as active descendant (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2965">#2965</a>)</li>
<li><strong>xl-docx-exporter:</strong> Clamp list nesting to the levels DOCX defines (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2969">#2969</a>)</li>
</ul>
<h3>❤️ Thank You</h3>
<ul>
<li>Adarshsm <a href="mailto:adarshmudugal@gmail.com">adarshmudugal@gmail.com</a></li>
<li>Nick The Sick (<a href="https://github.com/nperez0111"><code>@​nperez0111</code></a>)</li>
<li>Pupuking723 <a href="mailto:2318857637@qq.com">2318857637@qq.com</a></li>
</ul>
<h2>0.53.0 (2026-08-06)</h2>
<h3>🚀 Features</h3>
<ul>
<li><strong>shadcn:</strong> ⚠️ Use base-ui instead of radix (BLO-1279) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2913">#2913</a>)</li>
</ul>
<h3>🩹 Fixes</h3>
<ul>
<li>getCellSelection throwing error in positions (BLO-1193) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2911">#2911</a>)</li>
<li>Multi-column slash menu items within a column (BLO-905) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2914">#2914</a>)</li>
<li>Suggestion menu behaviour (BLO-1283, BLO-955) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2930">#2930</a>)</li>
<li>Ignore useless block/inline content mutations (BLO-1224) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2912">#2912</a>)</li>
<li><strong>slash-menu:</strong> Better overflow behavior (BLO-1192) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2909">#2909</a>)</li>
<li>Slash menu item selection behaviour (BLO-1222) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2838">#2838</a>)</li>
<li>HTML export/parse round trip ignoring empty blocks (BLO-873) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2931">#2931</a>)</li>
<li><strong>core:</strong> Guard getBlock() calls to prevent TypeError on stale blocks (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2941">#2941</a>)</li>
<li>Stop stale node view positions crashing the editor (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2938">#2938</a>)</li>
<li>Multi-column trailing blocks, column hover borders &amp; drop cursor left edge BLO-1226 (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2885">#2885</a>)</li>
</ul>
<h4>⚠️ Breaking Changes</h4>
<ul>
<li><strong>shadcn:</strong> ⚠️ Use base-ui instead of radix (BLO-1279) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2913">#2913</a>)</li>
</ul>
<h3>❤️ Thank You</h3>
<ul>
<li>Yousef</li>
<li>Nick Perez <a href="mailto:nick@blocknotejs.org">nick@blocknotejs.org</a></li>
<li>Matthew Lipski (<a href="https://github.com/matthewlipski"><code>@​matthewlipski</code></a>)</li>
</ul>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/ea5d80358f179d1683abcd2e0e3e9d547bf52eef"><code>ea5d803</code></a> chore(release): v0.54.0</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/99253c3814a93e6f5d1ae318efeb0b10df90f32d"><code>99253c3</code></a> chore: migrate to TypeScript 7 and consolidate the <a href="https://github.com/shared"><code>@​shared</code></a> alias</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/529c3b02f6e413c362e96718dd712dd4b4c495a0"><code>529c3b0</code></a> chore(release): v0.53.0</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/47d864c6e997963281af4df5ec54a4421773c134"><code>47d864c</code></a> fix(slash-menu): better overflow behavior (BLO-1192) (<a href="https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/mantine/issues/2909">#2909</a>)</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/8288b926e8a34737f287da1310e709b4785e2461"><code>8288b92</code></a> style: grid suggestion menu item padding (BLO-1225) (<a href="https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/mantine/issues/2910">#2910</a>)</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/dee7880b89b1e9bc00b4f4481f32652c7a4b4408"><code>dee7880</code></a> chore(release): v0.52.1</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/a99aab441b5db07c35d9f5ce406ea1676c6314ca"><code>a99aab4</code></a> chore(release): v0.52.0</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/030dcf0d133d99a173b8fa44ceec11b07a82867e"><code>030dcf0</code></a> refactor(versioning): consolidate sidebar CSS into the shared stylesheet</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/ef34ecca53f6d4c6a3cb0fa4d1058424e9a9124f"><code>ef34ecc</code></a> refactor(ui): forward refs in AttributionTooltip implementations</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/161a6147c09b81a0fc5af97afcc8606111481e4a"><code>161a614</code></a> fix(versioning): make yhub history snapshot ids unique and fix grouping</li>
<li>Additional commits viewable in <a href="https://github.com/TypeCellOS/BlockNote/commits/v0.54.0/packages/mantine">compare view</a></li>
</ul>
</details>
<br />

Updates `@blocknote/react` from 0.51.0 to 0.54.0
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/TypeCellOS/BlockNote/releases">@​blocknote/react's releases</a>.</em></p>
<blockquote>
<h2>v0.54.0</h2>
<h2>0.54.0 (2026-08-13)</h2>
<p>💖 The math block and diagram block has been sponsored by <a href="https://www.numerique.gouv.fr/dinum/">DINUM</a> 🇫🇷</p>
<h3>Math Block</h3>
<p>A long requested feature, you can now add block &amp; inline math to a BlockNote editor. They are driven by <a href="https://katex.org/">Katex</a> &amp; support much of <a href="https://www.latex-project.org/">Latex</a> for all your notation needs.</p>
<p><a href="https://github.com/user-attachments/assets/8fb5790e-6922-4f02-a35f-27c791b877e8">https://github.com/user-attachments/assets/8fb5790e-6922-4f02-a35f-27c791b877e8</a></p>
<p><a href="https://www.blocknotejs.org/examples/custom-schema/math-block">Link to demo</a></p>
<h3>Diagram Block</h3>
<p>We've also added support for a diagram block driven by <a href="https://mermaid.js.org/">Mermaid.js</a>, allowing you to add diagramming to the editor.</p>
<p><a href="https://github.com/user-attachments/assets/0a64e98a-5bf0-4dec-b1a4-84ccf98f4a70">https://github.com/user-attachments/assets/0a64e98a-5bf0-4dec-b1a4-84ccf98f4a70</a></p>
<p><a href="https://www.blocknotejs.org/examples/custom-schema/diagram-block">Link to demo</a></p>
<h3>Source Block with Preview</h3>
<p>Both the Math block &amp; Diagram block are built on a primitive that you can build your own custom blocks from. The Source Block with Preview primitive allows you to build a pair of a block which renders content with an inline editor for the content being rendered. This can enable other sorts of preview-like features in the future, exposed as an API for you to build your own custom blocks with.</p>
<!-- raw HTML omitted -->
<!-- raw HTML omitted -->
<p><a href="https://www.blocknotejs.org/examples/custom-schema/source-with-preview">Link to demo</a></p>
<h3>🚀 Features</h3>
<ul>
<li>Adds a Math block (<a href="https://github.com/TypeCellOS/BlockNote/commit/2a34f7d70">2a34f7d70</a>)</li>
<li>Adds a Diagram block (<a href="https://github.com/TypeCellOS/BlockNote/commit/0fca0ee7a">0fca0ee7a</a>)</li>
<li><strong>core:</strong> Source-with-preview, syntax highlighting &amp; exporter images (<a href="https://github.com/TypeCellOS/BlockNote/commit/503c796d3">503c796d3</a>)</li>
</ul>
<h3>🩹 Fixes</h3>
<ul>
<li><strong>ai:</strong> Operations on collaborative documents (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2952">#2952</a>)</li>
<li><strong>ai:</strong> Operations on blocks containing comments (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2953">#2953</a>)</li>
<li><strong>pdf:</strong> Add custom font and fontFamily options for CJK (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2945">#2945</a>)</li>
<li>Expose first suggestion as active descendant (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2965">#2965</a>)</li>
<li><strong>xl-docx-exporter:</strong> Clamp list nesting to the levels DOCX defines (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2969">#2969</a>)</li>
</ul>
<h3>❤️ Thank You</h3>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Changelog</summary>
<p><em>Sourced from <a href="https://github.com/TypeCellOS/BlockNote/blob/main/CHANGELOG.md">@​blocknote/react's changelog</a>.</em></p>
<blockquote>
<h2>0.54.0 (2026-08-13)</h2>
<h3>🚀 Features</h3>
<ul>
<li>Adds a Math block (<a href="https://github.com/TypeCellOS/BlockNote/commit/2a34f7d70">2a34f7d70</a>)</li>
<li>Adds a Diagram block (<a href="https://github.com/TypeCellOS/BlockNote/commit/0fca0ee7a">0fca0ee7a</a>)</li>
<li><strong>core:</strong> Source-with-preview, syntax highlighting &amp; exporter images (<a href="https://github.com/TypeCellOS/BlockNote/commit/503c796d3">503c796d3</a>)</li>
</ul>
<h3>🩹 Fixes</h3>
<ul>
<li><strong>ai:</strong> Operations on collaborative documents (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2952">#2952</a>)</li>
<li><strong>ai:</strong> Operations on blocks containing comments (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2953">#2953</a>)</li>
<li><strong>pdf:</strong> Add custom font and fontFamily options for CJK (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2945">#2945</a>)</li>
<li>Expose first suggestion as active descendant (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2965">#2965</a>)</li>
<li><strong>xl-docx-exporter:</strong> Clamp list nesting to the levels DOCX defines (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2969">#2969</a>)</li>
</ul>
<h3>❤️ Thank You</h3>
<ul>
<li>Adarshsm <a href="mailto:adarshmudugal@gmail.com">adarshmudugal@gmail.com</a></li>
<li>Nick The Sick (<a href="https://github.com/nperez0111"><code>@​nperez0111</code></a>)</li>
<li>Pupuking723 <a href="mailto:2318857637@qq.com">2318857637@qq.com</a></li>
</ul>
<h2>0.53.0 (2026-08-06)</h2>
<h3>🚀 Features</h3>
<ul>
<li><strong>shadcn:</strong> ⚠️ Use base-ui instead of radix (BLO-1279) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2913">#2913</a>)</li>
</ul>
<h3>🩹 Fixes</h3>
<ul>
<li>getCellSelection throwing error in positions (BLO-1193) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2911">#2911</a>)</li>
<li>Multi-column slash menu items within a column (BLO-905) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2914">#2914</a>)</li>
<li>Suggestion menu behaviour (BLO-1283, BLO-955) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2930">#2930</a>)</li>
<li>Ignore useless block/inline content mutations (BLO-1224) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2912">#2912</a>)</li>
<li><strong>slash-menu:</strong> Better overflow behavior (BLO-1192) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2909">#2909</a>)</li>
<li>Slash menu item selection behaviour (BLO-1222) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2838">#2838</a>)</li>
<li>HTML export/parse round trip ignoring empty blocks (BLO-873) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2931">#2931</a>)</li>
<li><strong>core:</strong> Guard getBlock() calls to prevent TypeError on stale blocks (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2941">#2941</a>)</li>
<li>Stop stale node view positions crashing the editor (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2938">#2938</a>)</li>
<li>Multi-column trailing blocks, column hover borders &amp; drop cursor left edge BLO-1226 (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2885">#2885</a>)</li>
</ul>
<h4>⚠️ Breaking Changes</h4>
<ul>
<li><strong>shadcn:</strong> ⚠️ Use base-ui instead of radix (BLO-1279) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2913">#2913</a>)</li>
</ul>
<h3>❤️ Thank You</h3>
<ul>
<li>Yousef</li>
<li>Nick Perez <a href="mailto:nick@blocknotejs.org">nick@blocknotejs.org</a></li>
<li>Matthew Lipski (<a href="https://github.com/matthewlipski"><code>@​matthewlipski</code></a>)</li>
</ul>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/ea5d80358f179d1683abcd2e0e3e9d547bf52eef"><code>ea5d803</code></a> chore(release): v0.54.0</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/503c796d37f2c8734cf65e9bad3348127043c63b"><code>503c796</code></a> feat(core): source-with-preview, syntax highlighting &amp; exporter images</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/99253c3814a93e6f5d1ae318efeb0b10df90f32d"><code>99253c3</code></a> chore: migrate to TypeScript 7 and consolidate the <a href="https://github.com/shared"><code>@​shared</code></a> alias</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/115d4333660a15391eea073ac7e7dd3ddb9da69a"><code>115d433</code></a> fix: expose first suggestion as active descendant (<a href="https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/react/issues/2965">#2965</a>)</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/bea469e31eab19242b1238cd3600a14c1d6148c1"><code>bea469e</code></a> refactor: vendor <code>@​tanstack/store</code> as a first-party Store (<a href="https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/react/issues/2956">#2956</a>)</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/529c3b02f6e413c362e96718dd712dd4b4c495a0"><code>529c3b0</code></a> chore(release): v0.53.0</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/d998f0168abd54ec57239479ea2dfc3d17df6a1a"><code>d998f01</code></a> fix: multi-column trailing blocks, column hover borders &amp; drop cursor left ed...</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/58d43ff08806ce078f03cf5a28afeefb1bede482"><code>58d43ff</code></a> fix: stop stale node view positions crashing the editor (<a href="https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/react/issues/2938">#2938</a>)</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/c32f9680082dc57c4bb2782a424ac67574a5713c"><code>c32f968</code></a> fix(core): guard getBlock() calls to prevent TypeError on stale blocks (<a href="https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/react/issues/2941">#2941</a>)</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/dee7880b89b1e9bc00b4f4481f32652c7a4b4408"><code>dee7880</code></a> chore(release): v0.52.1</li>
<li>Additional commits viewable in <a href="https://github.com/TypeCellOS/BlockNote/commits/v0.54.0/packages/react">compare view</a></li>
</ul>
</details>
<br />

Updates `@codemirror/lang-html` from 6.4.11 to 6.4.12
<details>
<summary>Commits</summary>
<ul>
<li>See full diff in <a href="https://github.com/codemirror/lang-html/commits">compare view</a></li>
</ul>
</details>
<br />

Updates `@eigenpal/docx-editor-agents` from 1.0.3 to 1.9.0
<details>
<summary>Commits</summary>
<ul>
<li>See full diff in <a href="https://github.com/eigenpal/docx-editor/commits/HEAD/packages/agents">compare view</a></li>
</ul>
</details>
<br />

Updates `@eigenpal/docx-editor-core` from 1.0.3 to 1.9.0
<details>
<summary>Commits</summary>
<ul>
<li>See full diff in <a href="https://github.com/eigenpal/docx-editor/commits/HEAD/packages/core">compare view</a></li>
</ul>
</details>
<br />

Updates `@eigenpal/docx-editor-i18n` from 1.0.3 to 1.9.0
<details>
<summary>Changelog</summary>
<p><em>Sourced from <a href="https://github.com/eigenpal/docx-editor/blob/main/packages/i18n/CHANGELOG.md">@​eigenpal/docx-editor-i18n's changelog</a>.</em></p>
<blockquote>
<h2>1.9.0</h2>
<h3>Patch Changes</h3>
<ul>
<li>28876a2: Make regular expressions over file- and library-supplied strings run in linear time and escape quoted font names completely. The variable-detection, plural-message, and core-properties date regexes no longer backtrack polynomially on hostile input, and font family names are now backslash-escaped before being wrapped in a quoted CSS string so a crafted DOCX font name cannot break out of it.</li>
</ul>
<h2>1.8.3</h2>
<h2>1.8.2</h2>
<h2>1.8.1</h2>
<h2>1.8.0</h2>
<h2>1.7.0</h2>
<h2>1.6.2</h2>
<h2>1.6.1</h2>
<h3>Patch Changes</h3>
<ul>
<li>c25ba18: Fix Indonesian (id) locale interpolation: restore the <code>{total}</code>, <code>{minRows}/{maxRows}/{minCols}/{maxCols}</code>, and <code>{label}</code> placeholders that were renamed or dropped, so the find/replace match count, insert-table validation hint, and line-spacing tooltip render their values instead of literal braces.</li>
<li>4a75c5e: Add Indonesian (id) community-maintained locale - 97% Coverage</li>
</ul>
<h2>1.6.0</h2>
<h2>1.5.0</h2>
<h2>1.4.0</h2>
<h2>1.3.3</h2>
<h2>1.3.2</h2>
<h2>1.3.1</h2>
<h2>1.3.0</h2>
<h2>1.2.1</h2>
<h2>1.2.0</h2>
<h2>1.1.0</h2>
<h3>Minor Changes</h3>
<ul>
<li>a7f9ac5: Add French locale</li>
<li>42ea72d: Track structural edits as OOXML revisions in suggesting mode. Paragraph-break insert/delete, paragraph-property changes, and table row/cell insert/delete/merge are now recorded, round-tripped through DOCX, and shown in the tracked-changes sidebar (React and Vue, localized). Adds <code>acceptChangeById(id)</code> / <code>rejectChangeById(id)</code>, and <code>acceptAllChanges</code> / <code>rejectAllChanges</code> now resolve every revision type rather than inline marks only. Fixes <a href="https://github.com/eigenpal/docx-editor/tree/HEAD/packages/i18n/issues/614">#614</a>.</li>
</ul>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li>See full diff in <a href="https://github.com/eigenpal/docx-editor/commits/HEAD/packages/i18n">compare view</a></li>
</ul>
</details>
<br />

Updates `@eigenpal/docx-editor-react` from 1.0.3 to 1.9.0
<details>
<summary>Changelog</summary>
<p><em>Sourced from <a href="https://github.com/eigenpal/docx-editor/blob/main/packages/react/CHANGELOG.md">@​eigenpal/docx-editor-react's changelog</a>.</em></p>
<blockquote>
<h2>1.9.0</h2>
<h3>Patch Changes</h3>
<ul>
<li>f61435b: Harden <code>openPrintWindow</code> to build the print window via DOM APIs instead of <code>document.write</code>, so a crafted document title cannot break out into executable markup. The framework-agnostic print helpers are now exported from <code>@docx-editor.dev/core</code> as the single source of truth, and the React package re-exports them unchanged.</li>
<li>791b132: Remove two potential slow-input denial-of-service paths in the React adapter. The data URL MIME parser now uses index math instead of a backtracking regex, and the toolbar test-id helper no longer scans across unmatched parentheses, so neither degrades on long crafted input.</li>
<li>Updated dependencies [4b47daf]</li>
<li>Updated dependencies [9144b69]</li>
<li>Updated dependencies [826aa32]</li>
<li>Updated dependencies [826aa32]</li>
<li>Updated dependencies [12c1f87]</li>
<li>Updated dependencies [7839ee9]</li>
<li>Updated dependencies [826aa32]</li>
<li>Updated dependencies [9454c9a]</li>
<li>Updated dependencies [f61435b]</li>
<li>Updated dependencies [28876a2]
<ul>
<li><a href="https://github.com/docx-editor"><code>@​docx-editor</code></a>.dev/core@1.9.0</li>
<li><a href="https://github.com/docx-editor"><code>@​docx-editor</code></a>.dev/i18n@1.9.0</li>
<li><a href="https://github.com/docx-editor"><code>@​docx-editor</code></a>.dev/agents@1.9.0</li>
</ul>
</li>
</ul>
<h2>1.8.3</h2>
<h3>Patch Changes</h3>
<ul>
<li>5ce3faa: Escape embedded font-family names before interpolating into the injected <code>@font-face</code> stylesheet, and build the print window via DOM APIs instead of <code>document.write</code> string concatenation. Prevents CSS injection and print-time XSS from crafted DOCX font names.</li>
<li>Updated dependencies [88a7650]</li>
<li>Updated dependencies [5ce3faa]</li>
<li>Updated dependencies [5eb0a43]</li>
<li>Updated dependencies [673e917]</li>
<li>Updated dependencies [74e36ef]</li>
<li>Updated dependencies [447d5b0]
<ul>
<li><a href="https://github.com/docx-editor"><code>@​docx-editor</code></a>.dev/core@1.8.3</li>
<li><a href="https://github.com/docx-editor"><code>@​docx-editor</code></a>.dev/agents@1.8.3</li>
<li><a href="https://github.com/docx-editor"><code>@​docx-editor</code></a>.dev/i18n@1.8.3</li>
</ul>
</li>
</ul>
<h2>1.8.2</h2>
<h3>Patch Changes</h3>
<ul>
<li>
<p>7811a73: Fix caret size and table insert button position when the editor is zoomed. Both are painted inside the zoomed page container, so their geometry is now normalized by the zoom factor instead of being scaled twice.</p>
<p>Fixes <a href="https://github.com/eigenpal/docx-editor/tree/HEAD/packages/react/issues/928">#928</a></p>
</li>
<li>
<p>Updated dependencies [4f183b3]</p>
</li>
<li>
<p>Updated dependencies [0c233db]</p>
</li>
<li>
<p>Updated dependencies [7811a73]</p>
<ul>
<li><a href="https://github.com/docx-editor"><code>@​docx-editor</code></a>.dev/core@1.8.2</li>
<li><a href="https://github.com/docx-editor"><code>@​docx-editor</code></a>.dev/agents@1.8.2</li>
<li><a href="https://github.com/docx-editor"><code>@​docx-editor</code></a>.dev/i18n@1.8.2</li>
</ul>
</li>
</ul>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li>See full diff in <a href="https://github.com/eigenpal/docx-editor/commits/HEAD/packages/react">compare view</a></li>
</ul>
</details>
<br />

Updates `@fortawesome/fontawesome-free` from 7.2.0 to 7.3.1
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/FortAwesome/Font-Awesome/releases">@​fortawesome/fontawesome-free's releases</a>.</em></p>
<blockquote>
<h2>Release 7.3.1</h2>
<p><strong>Change log available at <a href="https://fontawesome.com/docs/changelog/">https://fontawesome.com/docs/changelog/</a></strong></p>
<h2>Release 7.3.0</h2>
<p><strong>Change log available at <a href="https://fontawesome.com/docs/changelog/">https://fontawesome.com/docs/changelog/</a></strong></p>
</blockquote>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/FortAwesome/Font-Awesome/commit/14c65a3747d0f3b751f15831fc719236aea8729d"><code>14c65a3</code></a> Release 7.3.1 (<a href="https://redirect.github.com/FortAwesome/Font-Awesome/issues/21630">#21630</a>)</li>
<li><a href="https://github.com/FortAwesome/Font-Awesome/commit/70fb2dd154b617f62fc4ae5b0b7e2943bfd2aa96"><code>70fb2dd</code></a> Release 7.3.0 (<a href="https://redirect.github.com/FortAwesome/Font-Awesome/issues/21612">#21612</a>)</li>
<li>See full diff in <a href="https://github.com/FortAwesome/Font-Awesome/compare/7.2.0...7.3.1">compare view</a></li>
</ul>
</details>
<details>
<summary>Maintainer changes</summary>
<p>This version was pushed to npm by <a href="https://www.npmjs.com/~fortawesome-admin">fortawesome-admin</a>, a new releaser for <code>@​fortawesome/fontawesome-free</code> since your current version.</p>
</details>
<br />

Updates `@hocuspocus/provider` from 4.0.0 to 4.6.0
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/ueberdosis/hocuspocus/releases">@​hocuspocus/provider's releases</a>.</em></p>
<blockquote>
<h2>v4.6.0</h2>
<p>extension-redis will now slightly (setImmediate) delay forwarding messages to Redis, which improves performance a lot when many (500+) users are connected to the same document.</p>
<h2>What's Changed</h2>
<ul>
<li>feat/redis pending flushes by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1135">ueberdosis/hocuspocus#1135</a></li>
<li>fix: encode stateless message once when received operation via Redis … by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1136">ueberdosis/hocuspocus#1136</a></li>
</ul>
<p><strong>Full Changelog</strong>: <a href="https://github.com/ueberdosis/hocuspocus/compare/v4.5.0...v4.6.0">https://github.com/ueberdosis/hocuspocus/compare/v4.5.0...v4.6.0</a></p>
<h2>v4.5.0</h2>
<h2>What's Changed</h2>
<ul>
<li>feat: batch updates before sending to clients by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1130">ueberdosis/hocuspocus#1130</a></li>
<li>fix: ignore message in awarenessUpdateHandler if origin=this by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1129">ueberdosis/hocuspocus#1129</a></li>
<li>fix: when beforeHandleMessage throws, we don't want to process other messages that were already queued by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1123">ueberdosis/hocuspocus#1123</a></li>
</ul>
<p><strong>Full Changelog</strong>: <a href="https://github.com/ueberdosis/hocuspocus/compare/v4.4.0...v4.5.0">https://github.com/ueberdosis/hocuspocus/compare/v4.4.0...v4.5.0</a></p>
<h2>v4.4.0</h2>
<h2>What's Changed</h2>
<ul>
<li>feat: add <code>flushDelay</code> option for batching updates to reduce websocket traffic during heavy editing by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1118">ueberdosis/hocuspocus#1118</a></li>
<li>feat: add consistent state synchronization across Redis instances by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1119">ueberdosis/hocuspocus#1119</a></li>
<li>fix: make sure server.destroy() only runs once by <a href="https://github.com/DefV"><code>@​DefV</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1114">ueberdosis/hocuspocus#1114</a></li>
<li>fix: allow binding the server to a specific address by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1121">ueberdosis/hocuspocus#1121</a></li>
<li>build(deps): bump actions/checkout from 6 to 7 by <a href="https://github.com/dependabot"><code>@​dependabot</code></a>[bot] in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1117">ueberdosis/hocuspocus#1117</a></li>
<li>build(deps): bump hono from 4.12.21 to 4.12.25 by <a href="https://github.com/dependabot"><code>@​dependabot</code></a>[bot] in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1116">ueberdosis/hocuspocus#1116</a></li>
<li>build(deps): bump ws from 8.19.0 to 8.21.0 by <a href="https://github.com/dependabot"><code>@​dependabot</code></a>[bot] in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1115">ueberdosis/hocuspocus#1115</a></li>
</ul>
<h2>New Contributors</h2>
<ul>
<li><a href="https://github.com/DefV"><code>@​DefV</code></a> made their first contribution in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1114">ueberdosis/hocuspocus#1114</a></li>
</ul>
<p><strong>Full Changelog</strong>: <a href="https://github.com/ueberdosis/hocuspocus/compare/v4.3.0...v4.4.0">https://github.com/ueberdosis/hocuspocus/compare/v4.3.0...v4.4.0</a></p>
<h2>v4.3.0</h2>
<h2>What's Changed</h2>
<ul>
<li>feat: add <code>afterHandleMessage</code> hook to run after message handling completion by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1112">ueberdosis/hocuspocus#1112</a></li>
<li>feat: enforce pre-auth resource limits to safeguard server stability by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1113">ueberdosis/hocuspocus#1113</a></li>
</ul>
<p><strong>Full Changelog</strong>: <a href="https://github.com/ueberdosis/hocuspocus/compare/v4.2.0...v4.3.0">https://github.com/ueberdosis/hocuspocus/compare/v4.2.0...v4.3.0</a></p>
<h2>v4.2.0</h2>
<h2>What's Changed</h2>
<ul>
<li>feat: add <code>unloadImmediately</code> option to <code>disconnect()</code> for configurable document persistence behavior by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1111">ueberdosis/hocuspocus#1111</a></li>
</ul>
<p><strong>Full Changelog</strong>: <a href="https://github.com/ueberdosis/hocuspocus/compare/v4.1.2...v4.2.0">https://github.com/ueberdosis/hocuspocus/compare/v4.1.2...v4.2.0</a></p>
<h2>v4.1.2</h2>
<h2>What's Changed</h2>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Changelog</summary>
<p><em>Sourced from <a href="https://github.com/ueberdosis/hocuspocus/blob/main/CHANGELOG.md">@​hocuspocus/provider's changelog</a>.</em></p>
<blockquote>
<h1><a href="https://github.com/ueberdosis/hocuspocus/compare/v4.5.0...v4.6.0">4.6.0</a> (2026-08-10)</h1>
<h3>Bug Fixes</h3>
<ul>
<li>encode stateless message once when received operation via Redis ; this is a performance fix. (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1136">#1136</a>) (<a href="https://github.com/ueberdosis/hocuspocus/commit/b524b4b30299a64ffa1309f70a0fd6e761103d4a">b524b4b</a>)</li>
</ul>
<h1><a href="https://github.com/ueberdosis/hocuspocus/compare/v4.4.0...v4.5.0">4.5.0</a> (2026-08-04)</h1>
<h3>Bug Fixes</h3>
<ul>
<li>audit (<a href="https://github.com/ueberdosis/hocuspocus/commit/141360c256022deb5578c3902c3dfe0af8f6516e">141360c</a>)</li>
<li>flawky test relying on timings (<a href="https://github.com/ueberdosis/hocuspocus/commit/fe4a8e68801f1659624f53da745e595ad9f11c63">fe4a8e6</a>)</li>
<li>ignore message in awarenessUpdateHandler if origin=this (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1129">#1129</a>) (<a href="https://github.com/ueberdosis/hocuspocus/commit/08b25d4b258d932c68c999c14edcb4efc65c7a9b">08b25d4</a>)</li>
<li>update packages via audit --fix (<a href="https://github.com/ueberdosis/hocuspocus/commit/1dc9ca0ff35f1033136473d134cee8cb6b336281">1dc9ca0</a>)</li>
<li>when beforeHandleMessage throws, we don't want to process other messages that were already queued (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1123">#1123</a>) (<a href="https://github.com/ueberdosis/hocuspocus/commit/ed5dc40581cc829a6d0b04040717a8ee89296140">ed5dc40</a>)</li>
</ul>
<h3>Features</h3>
<ul>
<li>pnpm11 (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1133">#1133</a>) (<a href="https://github.com/ueberdosis/hocuspocus/commit/01c224ad9133340048c0e4f7bdce3981f4984d76">01c224a</a>)</li>
</ul>
<h1><a href="https://github.com/ueberdosis/hocuspocus/compare/v4.3.0...v4.4.0">4.4.0</a> (2026-07-13)</h1>
<h3>Bug Fixes</h3>
<ul>
<li>allow binding the server to a specific address (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1121">#1121</a>) (<a href="https://github.com/ueberdosis/hocuspocus/commit/408127b1c090356cc9148a801f314a8e6f863b09">408127b</a>)</li>
</ul>
<h3>Features</h3>
<ul>
<li>add <code>flushDelay</code> option for batching updates to reduce websocket traffic during heavy editing (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1118">#1118</a>) (<a href="https://github.com/ueberdosis/hocuspocus/commit/75594c05d57d48f2f70d4c9440c28b8226bf95ac">75594c0</a>)</li>
<li>add consistent state synchronization across Redis instances (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1119">#1119</a>) (<a href="https://github.com/ueberdosis/hocuspocus/commit/0051a6cb7618290d1f574da7ad61da2be77f839d">0051a6c</a>)</li>
</ul>
<h1><a href="https://github.com/ueberdosis/hocuspocus/compare/v4.2.0...v4.3.0">4.3.0</a> (2026-06-18)</h1>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/5c85b91af99544630200c438bfc5594a574d912e"><code>5c85b91</code></a> v4.6.0</li>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/d55367e6d3c20167d1daf920aa1e1094909a58ba"><code>d55367e</code></a> Feat/redis pending flushes (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1135">#1135</a>)</li>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/b524b4b30299a64ffa1309f70a0fd6e761103d4a"><code>b524b4b</code></a> fix: encode stateless message once when received operation via Redis ; this i...</li>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/3ec608445b8e024e15759504cca9ff1f7b09edf8"><code>3ec6084</code></a> build(deps): bump pnpm/action-setup from 5 to 6.0.9 (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1131">#1131</a>)</li>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/7827bded7c9181513a3b7c94acbaee0e4059d066"><code>7827bde</code></a> v4.5.0</li>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/141360c256022deb5578c3902c3dfe0af8f6516e"><code>141360c</code></a> fix: audit</li>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/1dc9ca0ff35f1033136473d134cee8cb6b336281"><code>1dc9ca0</code></a> fix: update packages via audit --fix</li>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/01c224ad9133340048c0e4f7bdce3981f4984d76"><code>01c224a</code></a> feat: pnpm11 (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1133">#1133</a>)</li>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/d9f87a6b738afa718dc0dd47580e02eacc764ce8"><code>d9f87a6</code></a> Feat/batch updates before sending to clients (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1130">#1130</a>)</li>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/a5812e6ec2fbeeefd6dd388a39e1d16fd192f6db"><code>a5812e6</code></a> chore: sync default port with playground</li>
<li>Additional commits viewable in <a href="https://github.com/ueberdosis/hocuspocus/compare/v4.0.0...v4.6.0">compare view</a></li>
</ul>
</details>
<br />

Updates `@mantine/charts` from 9.1.1 to 9.5.2
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/mantinedev/mantine/releases">@​mantine/charts's releases</a>.</em></p>
<blockquote>
<h2>9.5.2</h2>
<ul>
<li><code>[@mantine/hooks]</code> use-debounced-value: Fix <code>leading: true</code> firing multiple times per burst and emiting a stale value (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9119">#9119</a>)</li>
<li><code>[@mantine/schedule]</code> Fix recurring events not working with timzones (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9112">#9112</a>)</li>
<li><code>[@mantine/dates]</code> Fix <code>minDate</code> used for default date in some cases (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9117">#9117</a>)</li>
<li><code>[@mantine/core]</code> Tooltip: Fix tooltip setting NaN in top/left position style when event position values cannot be read (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9131">#9131</a>)</li>
<li><code>[@mantine/dates]</code> TimePicker: Fix incorrect focus handling of partially filled hours field (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9128">#9128</a>)</li>
<li><code>[@mantine/core]</code> RollingNumber: Fix incorrect copy event handling (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9132">#9132</a>)</li>
<li><code>[@mantine/core]</code> Notification: Fix incorrect <code>closeButtonProps</code> type (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9134">#9134</a>)</li>
<li><code>[@mantine/code-highlight]</code> Add support for lazy languages loading (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9141">#9141</a>)</li>
<li><code>[@mantine/code-highlight]</code> CodeHighlight: Add prop to keep indentation of the first line of the code block (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9140">#9140</a>)</li>
<li><code>[@mantine/dates]</code> Add missing formatting functions to MiniCalendarm DateInput and YarsList components</li>
<li><code>[@mantine/schedule]</code> WeekView: Improve performance of events positioning algorithm (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9075">#9075</a>)</li>
<li><code>[@mantine/form]</code> Add new useWatchValue hook</li>
<li><code>[@mantine/core]</code> Fix Combobox-based components not working correctly with Chrome autocomplete</li>
</ul>
<h2>9.5.1</h2>
<ul>
<li><code>[@mantine/tiptap]</code> Fix controls being initially disabledbefore element is focused</li>
<li><code>[@mantine/tiptap]</code> Fix source code control wrapping content with extra p tag</li>
<li><code>[@mantine/hooks]</code> use-scroll-spy: Allow usage with refs (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9025">#9025</a>)</li>
<li><code>[@mantine/core]</code> ColorInput: Add support for fullWidth prop (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9061">#9061</a>)</li>
<li><code>[@mantine/core]</code> Checkbox: Fix incottect indeterminate aria attributes handling in Checkbox.Card (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9095">#9095</a>)</li>
<li><code>[@mantine/core]</code> FloatingIndicator: Fix position and size calculation under scaled ancestors (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9071">#9071</a>)</li>
<li><code>[@mantine/core]</code> Tooltip: Add interactive prop support (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9072">#9072</a>)</li>
<li><code>[@mantine/core]</code> Cascader: Add safe area polygon support</li>
<li><code>[@mantine/core]</code> PasswordInput: Add option to change whether the visibility toggle is focusable (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9090">#9090</a>)</li>
<li><code>[@mantine/charts]</code> ScatterChart: Add option to add second y axis</li>
<li><code>[@mantine/schedule]</code> YearView: Add <code>renderDay</code> prop support</li>
<li><code>[@mantine/schedule]</code> YearView: Add option to hide weekend days</li>
<li><code>[@mantine/core]</code> InputWrapper: Fix <code>component: div</code> triggering typescript error if passed to <code>descriptionProps</code></li>
<li><code>[@mantine/schedule]</code> ResourcesMonthView: Add option to resize events</li>
<li><code>[@mantine/core]</code> FloatingWindow: Add support for  <code>onSizeChange</code> and <code>onResizeStart</code> props (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9085">#9085</a>)</li>
</ul>
<h2>9.5.0 🤖</h2>
<p><a href="https://mantine.dev/changelog/9-5-0">View changelog with demos on mantine.dev website</a></p>
<h2>Support Mantine development</h2>
<p>You can now sponsor Mantine development with <a href="https://opencollective.com/mantinedev">OpenCollective</a>.
All funds are used to improve Mantine and create new features and components.</p>
<h2>Migration to oxc</h2>
<p>Mantine has migrated its linting and formatting toolchain from ESLint and Prettier
to <a href="https://oxc.rs">oxc</a> – <a href="https://www.npmjs.com/package/oxlint">oxlint</a> is now used
as the linter and <a href="https://www.npmjs.com/package/oxfmt">oxfmt</a> as the formatter. Both
tools are written in Rust and are significantly faster than their predecessors, which
makes linting and formatting the entire codebase almost instant.</p>
<p>The shared configuration is available as a new
<a href="https://mantine.dev/oxc-config-mantine">oxc-config-mantine</a> package (a replacement for the previous</p>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/mantinedev/mantine/commit/8a284e2c2c53a9cb6f39f5dc389bf41b7a2073f8"><code>8a284e2</code></a> [release] Version: 9.5.2</li>
<li><a href="https://github.com/mantinedev/mantine/commit/0f57eaf5ae90c9e870fbb2a4cdd61a1d58c4c01d"><code>0f57eaf</code></a> [release] Version: 9.5.1</li>
<li><a href="https://github.com/mantinedev/mantine/commit/1e120595fdde5a414616df908bb3e600021d092e"><code>1e12059</code></a> [<code>@​mantine/charts</code>] ScatterChart: Add option to add second y axis</li>
<li><a href="https://github.com/mantinedev/mantine/commit/ca9bc6f156b63f1a10918d94ec31ec18e4e60546"><code>ca9bc6f</code></a> [release] Version: 9.5.1-alpha.1</li>
<li><a href="https://github.com/mantinedev/mantine/commit/8f1ad1bbe545c9cafafc5aef5b059d3d48e676a6"><code>8f1ad1b</code></a> [release] Version: 9.5.1-alpha.0</li>
<li><a href="https://github.com/mantinedev/mantine/commit/f1d330613f54dc9319d176e6d8ba5ebff233da18"><code>f1d3306</code></a> [release] Version: 9.5.0</li>
<li><a href="https://github.com/mantinedev/mantine/commit/732056219a0283f5822001981d7f652e632c4c87"><code>7320562</code></a> [release] Version: 9.4.3</li>
<li><a href="https://github.com/mantinedev/mantine/commit/170c45a5feed2386a464a7f05ae3daf6379cea04"><code>170c45a</code></a> Merge branch '9.5'</li>
<li><a href="https://github.com/mantinedev/mantine/commit/de21a8203060ba29441ab7623244339748e4319d"><code>de21a82</code></a> [release] Version: 9.4.3-alpha.0</li>
<li><a href="https://github.com/mantinedev/mantine/commit/e5752de4067bd58f6cdd970660b3c8469a56d4e5"><code>e5752de</code></a> [release] Version: 9.4.2</li>
<li>Additional commits viewable in <a href="https://github.com/mantinedev/mantine/commits/9.5.2/packages/@mantine/charts">compare view</a></li>
</ul>
</details>
<details>
<summary>Maintainer changes</summary>
<p>This version was pushed to npm by <a href="https://www.npmjs.com/~GitHub%20Actions">GitHub Actions</a>, a new releaser for <code>@​mantine/charts</code> since your current version.</p>
</details>
<br />

Updates `@mantine/colors-generator` from 9.1.1 to 9.5.2
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/mantinedev/mantine/releases">@​mantine/colors-generator's releases</a>.</em></p>
<blockquote>
<h2>9.5.2</h2>
<ul>
<li><code>[@mantine/hooks]</code> use-debounced-value: Fix <code>leading: true</code> firing multiple times per burst and emiting a stale value (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9119">#9119</a>)</li>
<li><code>[@mantine/schedule]</code> Fix recurring events not working with timzones (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9112">#9112</a>)</li>
<li><code>[@mantine/dates]</code> Fix <code>minDate</code> used for default date in some cases (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9117">#9117</a>)</li>
<li><code>[@mantine/core]</code> Tooltip: Fix tooltip setting NaN in top/left position style when event position values cannot be read (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9131">#9131</a>)</li>
<li><code>[@mantine/dates]</code> TimePicker: Fix incorrect focus handling of partially filled hours field (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9128">#9128</a>)</li>
<li><code>[@mantine/core]</code> RollingNumber: Fix incorrect copy event handling (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9132">#9132</a>)</li>
<li><code>[@mantine/core]</code> Notification: Fix incorrect <code>closeButtonProps</code> type (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9134">#9134</a>)</li>
<li><code>[@mantine/code-highlight]</code> Add support for lazy languages loading (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9141">#9141</a>)</li>
<li><code>[@mantine/code-highlight]</code> CodeHighlight: Add prop to keep indentation of the first line of the code block (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9140">#9140</a>)</li>
<li><code>[@mantine/dates]</code> Add missing formatting functions to MiniCalendarm DateInput and YarsList components</li>
<li><code>[@mantine/schedule]</code> WeekView: Improve performance of events positioning algorithm (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9075">#9075</a>)</li>
<li><code>[@mantine/form]</code> Add new useWatchValue hook</li>
<li><code>[@mantine/core]</code> Fix Combobox-based components not working correctly with Chrome autocomplete</li>
</ul>
<h2>9.5.1</h2>
<ul>
<li><code>[@mantine/tiptap]</code> Fix controls being initially disabledbefore element is focused</li>
<li><code>[@mantine/tiptap]</code> Fix source code control wrapping content with extra p tag</li>
<li><code>[@mantine/hooks]</code> use-scroll-spy: Allow usage with refs (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9025">#9025</a>)</li>
<li><code>[@mantine/core]</code> ColorInput: Add support for fullWidth prop (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9061">#9061</a>)</li>
<li><code>[@mantine/core]</code> Checkbox: Fix incottect indeterminate aria attributes handling in Checkbox.Card (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9095">#9095</a>)</li>
<li><code>[@mantine/core]</code> FloatingIndicator: Fix position and size calculation under scaled ancestors (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9071">#9071</a>)</li>
<li><code>[@mantine/core]</code> Tooltip: Add interactive prop support (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9072">#9072</a>)</li>
<li><code>[@mantine/core]</code> Cascader: Add safe area polygon support</li>
<li><code>[@...

_Description has been truncated_

<details><summary>Comment — nathanpond, 2026-08-31</summary>

**Held — not safe as a group** (reviewed 2026-08-31, local validation on top of `master`):
- `@blocknote/core` 0.51 → 0.54 removes `YjsThreadStore` and `User` from `@blocknote/core/comments`; `src/lib/yjs/commentAudit.ts`, `useBlockNoteWithYjs.ts` and `useResolveUsers.ts` fail to compile (`tsc -b` → 15 errors). This needs a code migration, not a dependency bump.
- `@eigenpal/docx-editor-*` 1.0.3 → 1.9.0: every version of all four packages is now marked **deprecated** on npm; 1.9.0 also drops the transitive `y-prosemirror` that `src/components/documents/DocxDocumentEditor.tsx:4` imports directly (`Cannot find module 'y-prosemirror'`). Deciding the future of the documents editor is a roadmap question.
- `@hocuspocus/provider` 4.0 → 4.6 must move together with the server in archived-104.

The rest of the group (Mantine 9.5.2, vite 8.2.2, axios 1.20.0, react 19.2.8, zod, tanstack, …) built cleanly in isolation. Suggested next step: `@dependabot ignore` BlockNote and docx-editor from this group (or exclude them in `dependabot.yml`) so the remaining 30+ updates can land; track the BlockNote comments-API migration and the docx-editor deprecation as issues.

</details>

<details><summary>Comment — dependabot[bot], 2026-08-31</summary>

Looks like these dependencies are updatable in another way, so this is no longer needed.

</details>

---

## archived-108 — chore(deps-dev): bump @eslint/js from 9.39.4 to 10.0.1 in /src/AutoNate.Spa

`CLOSED` · app/dependabot · opened 2026-08-31 · `dependabot/npm_and_yarn/src/AutoNate.Spa/eslint/js-10.0.1` → `master`

Bumps [@eslint/js](https://github.com/eslint/eslint/tree/HEAD/packages/js) from 9.39.4 to 10.0.1.
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/eslint/eslint/releases">@​eslint/js's releases</a>.</em></p>
<blockquote>
<h2>v10.0.1</h2>
<h2>Bug Fixes</h2>
<ul>
<li><a href="https://github.com/eslint/eslint/commit/c87d5bded54c5cf491eb04c24c9d09bbbd42c23e"><code>c87d5bd</code></a> fix: update eslint (<a href="https://github.com/eslint/eslint/tree/HEAD/packages/js/issues/20531">#20531</a>) (renovate[bot])</li>
<li><a href="https://github.com/eslint/eslint/commit/d84100115c14691691058f00779c94e74fca946a"><code>d841001</code></a> fix: update <code>minimatch</code> to <code>10.2.1</code> to address security vulnerabilities (<a href="https://github.com/eslint/eslint/tree/HEAD/packages/js/issues/20519">#20519</a>) (루밀LuMir)</li>
<li><a href="https://github.com/eslint/eslint/commit/04c21475b3004904948f02049f2888b401d82c78"><code>04c2147</code></a> fix: update error message for unused suppressions (<a href="https://github.com/eslint/eslint/tree/HEAD/packages/js/issues/20496">#20496</a>) (fnx)</li>
<li><a href="https://github.com/eslint/eslint/commit/38b089c1726feac0e31a31d47941bd99e29ce003"><code>38b089c</code></a> fix: update dependency <code>@​eslint/config-array</code> to ^0.23.1 (<a href="https://github.com/eslint/eslint/tree/HEAD/packages/js/issues/20484">#20484</a>) (renovate[bot])</li>
</ul>
<h2>Documentation</h2>
<ul>
<li><a href="https://github.com/eslint/eslint/commit/5b3dbce50a1404a9f118afe810cefeee79388a2a"><code>5b3dbce</code></a> docs: add AI acknowledgement section to templates (<a href="https://github.com/eslint/eslint/tree/HEAD/packages/js/issues/20431">#20431</a>) (루밀LuMir)</li>
<li><a href="https://github.com/eslint/eslint/commit/6f23076037d5879f20fb3be2ef094293b1e8d38c"><code>6f23076</code></a> docs: toggle nav in no-JS mode (<a href="https://github.com/eslint/eslint/tree/HEAD/packages/js/issues/20476">#20476</a>) (Tanuj Kanti)</li>
<li><a href="https://github.com/eslint/eslint/commit/b69cfb32a16c5d5e9986390d484fae1d21e406f9"><code>b69cfb3</code></a> docs: Update README (GitHub Actions Bot)</li>
</ul>
<h2>Chores</h2>
<ul>
<li><a href="https://github.com/eslint/eslint/commit/e5c281ffd038a3a7a3e5364db0b9378e0ad83020"><code>e5c281f</code></a> chore: updates for v9.39.3 release (Jenkins)</li>
<li><a href="https://github.com/eslint/eslint/commit/8c3832adb77cd993b4a24891900d5eeaaf093cdc"><code>8c3832a</code></a> chore: update <code>@​typescript-eslint/parser</code> to ^8.56.0 (<a href="https://github.com/eslint/eslint/tree/HEAD/packages/js/issues/20514">#20514</a>) (Milos Djermanovic)</li>
<li><a href="https://github.com/eslint/eslint/commit/8330d238ae6adb68bb6a1c9381e38cfedd990d94"><code>8330d23</code></a> test: add tests for config-api (<a href="https://github.com/eslint/eslint/tree/HEAD/packages/js/issues/20493">#20493</a>) (Milos Djermanovic)</li>
<li><a href="https://github.com/eslint/eslint/commit/37d6e91e88fa6a2ca6d8726679096acff21ba6cc"><code>37d6e91</code></a> chore: remove eslint v10 prereleases from eslint-config-eslint deps (<a href="https://github.com/eslint/eslint/tree/HEAD/packages/js/issues/20494">#20494</a>) (Milos Djermanovic)</li>
<li><a href="https://github.com/eslint/eslint/commit/da7cd0e79197ad16e17052eef99df141de6dbfb1"><code>da7cd0e</code></a> refactor: cleanup error message templates (<a href="https://github.com/eslint/eslint/tree/HEAD/packages/js/issues/20479">#20479</a>) (Francesco Trotta)</li>
<li><a href="https://github.com/eslint/eslint/commit/84fb885d49ac810e79a9491276b4828b53d913e5"><code>84fb885</code></a> chore: package.json update for <code>@​eslint/js</code> release (Jenkins)</li>
<li><a href="https://github.com/eslint/eslint/commit/1f667344b57c4c09b548d94bcfac1f91b6e5c63d"><code>1f66734</code></a> chore: add <code>eslint</code> to <code>peerDependencies</code> of <code>@eslint/js</code> (<a href="https://github.com/eslint/eslint/tree/HEAD/packages/js/issues/20467">#20467</a>) (Milos Djermanovic)</li>
</ul>
<h2>v10.0.0</h2>
<h2>Breaking Changes</h2>
<ul>
<li><a href="https://github.com/eslint/eslint/commit/f9e54f43a5e497cdfa179338b431093245cb787b"><code>f9e54f4</code></a> feat!: estimate rule-tester failure location (<a href="https://github.com/eslint/eslint/tree/HEAD/packages/js/issues/20420">#20420</a>) (ST-DDT)</li>
<li><a href="https://github.com/eslint/eslint/commit/a176319d8ade1a7d9b2d7fb8f038f55a2662325f"><code>a176319</code></a> feat!: replace <code>chalk</code> with <code>styleText</code> and add <code>color</code> to <code>ResultsMeta</code> (<a href="https://github.com/eslint/eslint/tree/HEAD/packages/js/issues/20227">#20227</a>) (루밀LuMir)</li>
<li><a href="https://github.com/eslint/eslint/commit/c7046e6c1e03c4ca0eee4888a1f2eba4c6454f84"><code>c7046e6</code></a> feat!: enable JSX reference tracking (<a href="https://github.com/eslint/eslint/tree/HEAD/packages/js/issues/20152">#20152</a>) (Pixel998)</li>
<li><a href="https://github.com/eslint/eslint/commit/fa31a608901684fbcd9906d1907e66561d16e5aa"><code>fa31a60</code></a> feat!: add <code>name</code> to configs (<a href="https://github.com/eslint/eslint/tree/HEAD/packages/js/issues/20015">#20015</a>) (Kirk Waiblinger)</li>
<li><a href="https://github.com/eslint/eslint/commit/3383e7ec9028166cafc8ea7986c2f7498d0049f0"><code>3383e7e</code></a> fix!: remove deprecated <code>SourceCode</code> methods (<a href="https://github.com/eslint/eslint/tree/HEAD/packages/js/issues/20137">#20137</a>) (Pixel998)</li>
<li><a href="https://github.com/eslint/eslint/commit/501abd0e916a35554c58b7c0365537f1fa3880ce"><code>501abd0</code></a> feat!: update dependency minimatch to v10 (<a href="https://github.com/eslint/eslint/tree/HEAD/packages/js/issues/20246">#20246</a>) (renovate[bot])</li>
<li><a href="https://github.com/eslint/eslint/commit/ca4d3b40085de47561f89656a2207d09946ed45e"><code>ca4d3b4</code></a> fix!: stricter rule tester assertions for valid test cases (<a href="https://github.com/eslint/eslint/tree/HEAD/packages/js/issues/20125">#20125</a>) (唯然)</li>
<li><a href="https://github.com/eslint/eslint/commit/96512a66c86402fb0538cdcb6cd30b9073f6bf3b"><code>96512a6</code></a> fix!: Remove deprecated rule context methods (<a href="https://github.com/eslint/eslint/tree/HEAD/packages/js/issues/20086">#20086</a>) (Nicholas C. Zakas)</li>
<li><a href="https://github.com/eslint/eslint/commit/c69fdacdb2e886b9d965568a397aa8220db3fe90"><code>c69fdac</code></a> feat!: remove eslintrc support (<a href="https://github.com/eslint/eslint/tree/HEAD/packages/js/issues/20037">#20037</a>) (Francesco Trotta)</li>
<li><a href="https://github.com/eslint/eslint/commit/208b5cc34a8374ff81412b5bec2e0800eebfbd04"><code>208b5cc</code></a> feat!: Use <code>ScopeManager#addGlobals()</code> (<a href="https://github.com/eslint/eslint/tree/HEAD/packages/js/issues/20132">#20132</a>) (Milos Djermanovic)</li>
<li><a href="https://github.com/eslint/eslint/commit/a2ee188ea7a38a0c6155f3d39e2b00e1d0f36e14"><code>a2ee188</code></a> fix!: add <code>uniqueItems: true</code> in <code>no-invalid-regexp</code> option (<a href="https://github.com/eslint/eslint/tree/HEAD/packages/js/issues/20155">#20155</a>) (Tanuj Kanti)</li>
<li><a href="https://github.com/eslint/eslint/commit/a89059dbf2832d417dd493ee81483227ec44e4ab"><code>a89059d</code></a> feat!: Program range span entire source text (<a href="https://github.com/eslint/eslint/tree/HEAD/packages/js/issues/20133">#20133</a>) (Pixel998)</li>
<li><a href="https://github.com/eslint/eslint/commit/39a6424373d915fa9de0d7b0caba9a4dc3da9b53"><code>39a6424</code></a> fix!: assert 'text' is a string across all RuleFixer methods (<a href="https://github.com/eslint/eslint/tree/HEAD/packages/js/issues/20082">#20082</a>) (Pixel998)</li>
<li><a href="https://github.com/eslint/eslint/commit/f28fbf846244e043c92b355b224d121b06140b44"><code>f28fbf8</code></a> fix!: Deprecate <code>&quot;always&quot;</code> and <code>&quot;as-needed&quot;</code> options of the <code>radix</code> rule (<a href="https://github.com/eslint/eslint/tree/HEAD/packages/js/issues/20223">#20223</a>) (Milos Djermanovic)</li>
<li><a href="https://github.com/eslint/eslint/commit/aa3fb2b233e929b37220be940575f42c280e0b98"><code>aa3fb2b</code></a> fix!: tighten <code>func-names</code> schema (<a href="https://github.com/eslint/eslint/tree/HEAD/packages/js/issues/20119">#20119</a>) (Pixel998)</li>
<li><a href="https://github.com/eslint/eslint/commit/f6c0ed0311dcfee853367d5068c765d066e6b756"><code>f6c0ed0</code></a> feat!: report <code>eslint-env</code> comments as errors (<a href="https://github.com/eslint/eslint/tree/HEAD/packages/js/issues/20128">#20128</a>) (Francesco Trotta)</li>
<li><a href="https://github.com/eslint/eslint/commit/4bf739fb533e59f7f0a66b65f7bc80be0f37d8db"><code>4bf739f</code></a> fix!: remove deprecated <code>LintMessage#nodeType</code> and <code>TestCaseError#type</code> (<a href="https://github.com/eslint/eslint/tree/HEAD/packages/js/issues/20096">#20096</a>) (Pixel998)</li>
<li><a href="https://github.com/eslint/eslint/commit/523c076866400670fb2192a3f55dbf7ad3469247"><code>523c076</code></a> feat!: drop support for jiti &lt; 2.2.0 (<a href="https://github.com/eslint/eslint/tree/HEAD/packages/js/issues/20016">#20016</a>) (michael faith)</li>
<li><a href="https://github.com/eslint/eslint/commit/454a292c95f34dad232411ddac06408e6383bb64"><code>454a292</code></a> feat!: update <code>eslint:recommended</code> configuration (<a href="https://github.com/eslint/eslint/tree/HEAD/packages/js/issues/20210">#20210</a>) (Pixel998)</li>
<li><a href="https://github.com/eslint/eslint/commit/4f880ee02992e1bf0e96ebaba679985e2d1295f1"><code>4f880ee</code></a> feat!: remove <code>v10_*</code> and inactive <code>unstable_*</code> flags (<a href="https://github.com/eslint/eslint/tree/HEAD/packages/js/issues/20225">#20225</a>) (sethamus)</li>
<li><a href="https://github.com/eslint/eslint/commit/f18115c363a4ac7671a4c7f30ee13d57ebba330f"><code>f18115c</code></a> feat!: <code>no-shadow-restricted-names</code> report <code>globalThis</code> by default (<a href="https://github.com/eslint/eslint/tree/HEAD/packages/js/issues/20027">#20027</a>) (sethamus)</li>
<li><a href="https://github.com/eslint/eslint/commit/c6358c31fbd3937b92d89be2618ffdf5a774604e"><code>c6358c3</code></a> feat!: Require Node.js <code>^20.19.0 || ^22.13.0 || &gt;=24</code> (<a href="https://github.com/eslint/eslint/tree/HEAD/packages/js/issues/20160">#20160</a>) (Milos Djermanovic)</li>
</ul>
<h2>Features</h2>
<ul>
<li><a href="https://github.com/eslint/eslint/commit/bff9091927811497dbf066b0e3b85ecb37d43822"><code>bff9091</code></a> feat: handle <code>Array.fromAsync</code> in <code>array-callback-return</code> (<a href="https://github.com/eslint/eslint/tree/HEAD/packages/js/issues/20457">#20457</a>) (Francesco Trotta)</li>
<li><a href="https://github.com/eslint/eslint/commit/290c594bb50c439fb71bc75521ee5360daa8c222"><code>290c594</code></a> feat: add <code>self</code> to <code>no-implied-eval</code> rule (<a href="https://github.com/eslint/eslint/tree/HEAD/packages/js/issues/20468">#20468</a>) (sethamus)</li>
<li><a href="https://github.com/eslint/eslint/commit/43677de07ebd6e14bfac40a46ad749ba783c45f2"><code>43677de</code></a> feat: fix handling of function and class expression names in <code>no-shadow</code> (<a href="https://github.com/eslint/eslint/tree/HEAD/packages/js/issues/20432">#20432</a>) (Milos Djermanovic)</li>
</ul>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/eslint/eslint/commit/84fb885d49ac810e79a9491276b4828b53d913e5"><code>84fb885</code></a> chore: package.json update for <code>@​eslint/js</code> release</li>
<li><a href="https://github.com/eslint/eslint/commit/1f667344b57c4c09b548d94bcfac1f91b6e5c63d"><code>1f66734</code></a> chore: add <code>eslint</code> to <code>peerDependencies</code> of <code>@eslint/js</code> (<a href="https://github.com/eslint/eslint/tree/HEAD/packages/js/issues/20467">#20467</a>)</li>
<li><a href="https://github.com/eslint/eslint/commit/f3fbc2f60cbe2c718364feb8c3fc0452c0df3c56"><code>f3fbc2f</code></a> chore: set <code>@eslint/js</code> version to 10.0.0 to skip releasing it (<a href="https://github.com/eslint/eslint/tree/HEAD/packages/js/issues/20466">#20466</a>)</li>
<li><a href="https://github.com/eslint/eslint/commit/b4b3127f8542c599ce2dea804b6582ebc40c993d"><code>b4b3127</code></a> chore: package.json update for <code>@​eslint/js</code> release</li>
<li><a href="https://github.com/eslint/eslint/commit/0b14059491d830a49b3577931f4f68fbcfce6be5"><code>0b14059</code></a> chore: package.json update for <code>@​eslint/js</code> release</li>
<li><a href="https://github.com/eslint/eslint/commit/fa31a608901684fbcd9906d1907e66561d16e5aa"><code>fa31a60</code></a> feat!: add <code>name</code> to configs (<a href="https://github.com/eslint/eslint/tree/HEAD/packages/js/issues/20015">#20015</a>)</li>
<li><a href="https://github.com/eslint/eslint/commit/1e2cad5f6fa47ed6ed89d2a29798dda926d50990"><code>1e2cad5</code></a> chore: package.json update for <code>@​eslint/js</code> release</li>
<li><a href="https://github.com/eslint/eslint/commit/454a292c95f34dad232411ddac06408e6383bb64"><code>454a292</code></a> feat!: update <code>eslint:recommended</code> configuration (<a href="https://github.com/eslint/eslint/tree/HEAD/packages/js/issues/20210">#20210</a>)</li>
<li><a href="https://github.com/eslint/eslint/commit/c6358c31fbd3937b92d89be2618ffdf5a774604e"><code>c6358c3</code></a> feat!: Require Node.js <code>^20.19.0 || ^22.13.0 || &gt;=24</code> (<a href="https://github.com/eslint/eslint/tree/HEAD/packages/js/issues/20160">#20160</a>)</li>
<li>See full diff in <a href="https://github.com/eslint/eslint/commits/v10.0.1/packages/js">compare view</a></li>
</ul>
</details>
<br />

<details><summary>Comment — nathanpond, 2026-08-31</summary>

**Held** (2026-08-31): `@eslint/js` 10 declares a peer dependency on `eslint` 10 and removes eslintrc/deprecated rule-context APIs; the SPA is on `eslint` ^9.39. Upgrade `eslint`, `@eslint/js` and `typescript-eslint` together in one change.

</details>

<details><summary>Comment — nathanpond, 2026-08-31</summary>

**Re-checked 2026-08-31 — still held, and now with the specific blockers named.** The original note said "removes eslintrc/deprecated rule-context APIs"; that is directionally right but the actual blocker is narrower and worth recording, because it decides *what we wait on*.

**The peer chain is satisfiable except for two plugins, both already at their latest release:**

| package | installed | latest | peer `eslint` |
|---|---|---|---|
| `typescript-eslint` | 8.68.0 | 8.69.0 | `^8.57 \|\| ^9 \|\| ^10` ✅ |
| `eslint-plugin-react-hooks` | 7.1.1 | 7.1.1 | `… \|\| ^9 \|\| ^10` ✅ |
| `eslint-plugin-jsx-a11y` | 6.10.2 | **6.10.2** | `^3 … ^9` ❌ |
| `eslint-plugin-react` | 7.37.5 | **7.37.5** | `^3 … ^9.7` ❌ |

So there is no version to upgrade *to* — this is not a coordination problem on our side, it is upstream not having shipped support.

**And it is a real break, not just a declared range.** Forcing it (`eslint@10.9.1` + `@eslint/js@10.0.1`, installed with `--legacy-peer-deps`) fails on the first file linted:

```
TypeError: Error while loading rule 'react/no-direct-mutation-state':
contextOrFilename.getFilename is not a function
  at resolveBasedir (node_modules/eslint-plugin-react/lib/util/version.js)
  at detectReactVersion (…)
```

`eslint-plugin-react` calls `context.getFilename()`, which ESLint 10 removed. A strict `npm install` refuses outright (`ERESOLVE`, `peerOptional eslint@"^10.0.0" from @eslint/js@10.0.1`).

**What to watch:** `jsx-eslint/eslint-plugin-jsx-a11y` has ESLint 10 support open (#1079, #1081; an earlier attempt #1077 was closed). `eslint-plugin-react` has no ESLint 10 release yet. When both ship, the upgrade is one change: `eslint` + `@eslint/js` + those two plugins together, then `npm run lint` must stay at 0 errors / ≤411 warnings.

Unblocked for the record: Node is fine (eslint 10 wants `^20.19 || ^22.13 || >=24`; we are on 24), and the SPA is already on flat config (`eslint.config.js`), so the eslintrc removal is a non-issue here.

</details>

<details><summary>Comment — nathanpond, 2026-09-01</summary>

Re-checked 2026-09-01. **Still blocked**, and the blocker has not moved.

Two problems, not one:

| Package | Declares | Project has |
|---|---|---|
| `@eslint/js@10.0.1` | `eslint: ^10.0.0` | `eslint: ^9.39.4` |
| `eslint-plugin-react@7.37.5` | `eslint: ^3 \|\| … \|\| ^9.7` | — |

So the bump alone is a peer mismatch, and the move to ESLint 10 that would resolve it is itself blocked by `eslint-plugin-react`, which still tops out at `^9.7`. Its `next` dist-tag is `7.8.0-rc.0` — a *lower* version than `latest` (7.37.5), i.e. a stale tag rather than a preview with ESLint 10 support.

The rest of the lint stack is already ready:

- `eslint-plugin-react-hooks@7.1.1` → `eslint: … || ^10.0.0` ✅
- `typescript-eslint@8.69.0` → `eslint: ^8.57.0 || ^9.0.0 || ^10.0.0` ✅

**`eslint-plugin-react` is the single remaining blocker.** Worth re-checking when it ships a release accepting `eslint@^10`.

Not forcing it: dropping `eslint-plugin-react` to unblock the bump would trade a working lint gate for a dependency version, and this repo leans on those gates — `npm run lint` carries the warning ratchet and the jsx-a11y error list added in archived-40.

</details>

<details><summary>Comment — nathanpond, 2026-09-01</summary>

Closing ahead of a planned git history rewrite (stripping a paid theme from history before the repo goes public). A rewrite rebases every commit, so this branch would be stranded.

Nothing is lost: the bump is still needed and Dependabot will re-open it against the new history. The upstream blocker recorded in the comment above is unchanged as of today, so there is no work to preserve here.

</details>

<details><summary>Comment — dependabot[bot], 2026-09-01</summary>

OK, I won't notify you again about this release, but will get in touch when a new version is available. If you'd rather skip all updates until the next major or minor version, let me know by commenting `@dependabot ignore this major version` or `@dependabot ignore this minor version`. You can also ignore all major, minor, or patch releases for a dependency by adding an [`ignore` condition](https://docs.github.com/en/code-security/supply-chain-security/configuration-options-for-dependency-updates#ignore) with the desired `update_types` to your config file.

If you change your mind, just re-open this PR and I'll resolve any conflicts on it.

</details>

---

## archived-109 — chore(deps-dev): bump typescript from 6.0.3 to 7.0.2 in /src/AutoNate.Spa

`CLOSED` · app/dependabot · opened 2026-08-31 · `dependabot/npm_and_yarn/src/AutoNate.Spa/typescript-7.0.2` → `master`

Bumps [typescript](https://github.com/microsoft/TypeScript) from 6.0.3 to 7.0.2.
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/microsoft/TypeScript/releases">typescript's releases</a>.</em></p>
<blockquote>
<h2>TypeScript 7.0.2</h2>
<p><a href="https://devblogs.microsoft.com/typescript/announcing-typescript-7-0/">https://devblogs.microsoft.com/typescript/announcing-typescript-7-0/</a></p>
<p>This tag was originally released at: <a href="https://github.com/microsoft/typescript-go/releases/tag/typescript%2Fv7.0.2">https://github.com/microsoft/typescript-go/releases/tag/typescript%2Fv7.0.2</a></p>
</blockquote>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/microsoft/TypeScript/commit/1e4744d68260a7cb91b62b12edc3f6a2187faaf1"><code>1e4744d</code></a> Merge branch 'main' into ts7-release</li>
<li><a href="https://github.com/microsoft/TypeScript/commit/a5a219c3b5da0db4fa0ecf6c0b1f588c9af9c669"><code>a5a219c</code></a><code>microsoft/typescript-go#4558</code></li>
<li><a href="https://github.com/microsoft/TypeScript/commit/ecfe30dce91368d52c9a49b6095bb0b673a238f8"><code>ecfe30d</code></a> Update status localization</li>
<li><a href="https://github.com/microsoft/TypeScript/commit/5de25b5f8fec2ca35eadaed041f1f06d2e214895"><code>5de25b5</code></a> Hide executable name in TypeScript status</li>
<li><a href="https://github.com/microsoft/TypeScript/commit/d7ce74a75da2b80e8201506a1599c06549432b93"><code>d7ce74a</code></a> Show bundled TypeScript version for packaged servers</li>
<li><a href="https://github.com/microsoft/TypeScript/commit/29be66a607707f90d7a53103a4469bb3015a4d54"><code>29be66a</code></a> Correct TS 7 release version to 7.0.2</li>
<li><a href="https://github.com/microsoft/TypeScript/commit/ed2bd1bfa4aac5211ce4bc58fcd1313c7eddc8ff"><code>ed2bd1b</code></a> Merge branch 'main' into ts7-release</li>
<li><a href="https://github.com/microsoft/TypeScript/commit/887307575c58ea640dbeba3b4e8fdb6347cd3044"><code>8873075</code></a> Bump the github-actions group across 1 directory with 3 updates (microsoft/ty...</li>
<li><a href="https://github.com/microsoft/TypeScript/commit/9427131ae2d4e230a90ee8a09daac4e75da3e311"><code>9427131</code></a> Set up stable / nightly extension split, other prep (microsoft/typescript-go#...</li>
<li><a href="https://github.com/microsoft/TypeScript/commit/d4eaca5460a1f5f02a829e62706794b0a6fb903e"><code>d4eaca5</code></a><code>microsoft/typescript-go#4549</code></li>
<li>Additional commits viewable in <a href="https://github.com/microsoft/TypeScript/compare/v6.0.3...v7.0.2">compare view</a></li>
</ul>
</details>
<details>
<summary>Maintainer changes</summary>
<p>This version was pushed to npm by <a href="https://www.npmjs.com/~microsoft1es">microsoft1es</a>, a new releaser for typescript since your current version.</p>
</details>
<br />

<details><summary>Comment — nathanpond, 2026-08-31</summary>

**Held** (2026-08-31): TypeScript 7 is the new native (Go) compiler — a major toolchain change. Not adopting it via a Dependabot bump; needs a deliberate upgrade of `typescript-eslint`, Vite plugin and build scripts together, verified across the SPA and both sidecars.

</details>

<details><summary>Comment — nathanpond, 2026-09-01</summary>

Re-checked 2026-09-01. **Still blocked**, and the blocker has not moved.

`typescript-eslint@8.69.0` declares:

```
typescript: '>=4.8.4 <6.1.0'
```

TypeScript 7.0.2 is outside that range. The `8.68.0 → 8.69.0` release since this PR was opened did **not** widen it, and the canary (`8.69.1-alpha.0`) declares the same range — so there is no pre-release path either.

Worth re-checking when typescript-eslint publishes a version whose `typescript` peer admits 7.x.

Not forcing it with an override: the type-aware rules run through typescript-eslint's own program, so pointing them at an unsupported compiler risks silently wrong lint results rather than a clean failure — and this repo relies on those rules.

</details>

<details><summary>Comment — nathanpond, 2026-09-01</summary>

Closing ahead of a planned git history rewrite (stripping a paid theme from history before the repo goes public). A rewrite rebases every commit, so this branch would be stranded.

Nothing is lost: the bump is still needed and Dependabot will re-open it against the new history. The upstream blocker recorded in the comment above is unchanged as of today, so there is no work to preserve here.

</details>

<details><summary>Comment — dependabot[bot], 2026-09-01</summary>

OK, I won't notify you again about this release, but will get in touch when a new version is available. If you'd rather skip all updates until the next major or minor version, let me know by commenting `@dependabot ignore this major version` or `@dependabot ignore this minor version`. You can also ignore all major, minor, or patch releases for a dependency by adding an [`ignore` condition](https://docs.github.com/en/code-security/supply-chain-security/configuration-options-for-dependency-updates#ignore) with the desired `update_types` to your config file.

If you change your mind, just re-open this PR and I'll resolve any conflicts on it.

</details>

---

## archived-110 — Bump react-markdown from 9.1.0 to 10.1.0 in /src/AutoNate.Spa

`MERGED (merged 2026-08-31)` · app/dependabot · opened 2026-08-31 · `dependabot/npm_and_yarn/src/AutoNate.Spa/react-markdown-10.1.0` → `master`

Bumps [react-markdown](https://github.com/remarkjs/react-markdown) from 9.1.0 to 10.1.0.
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/remarkjs/react-markdown/releases">react-markdown's releases</a>.</em></p>
<blockquote>
<h2>10.1.0</h2>
<h4>Add</h4>
<ul>
<li>939c667 Add <code>fallback</code> prop to <code>MarkdownHooks</code>
by <a href="https://github.com/remcohaszing"><code>@​remcohaszing</code></a> in <a href="https://redirect.github.com/remarkjs/react-markdown/pull/897">remarkjs/react-markdown#897</a></li>
</ul>
<h4>Fix</h4>
<ul>
<li>a40ae2e Fix race condition in <code>MarkdownHooks</code>
by <a href="https://github.com/remcohaszing"><code>@​remcohaszing</code></a> in <a href="https://redirect.github.com/remarkjs/react-markdown/pull/896">remarkjs/react-markdown#896</a></li>
</ul>
<p><strong>Full Changelog</strong>: <a href="https://github.com/remarkjs/react-markdown/compare/10.0.1...10.1.0">https://github.com/remarkjs/react-markdown/compare/10.0.1...10.1.0</a></p>
<h2>10.0.1</h2>
<ul>
<li>7c17ede Fix TypeScript performance around components
by <a href="https://github.com/remcohaszing"><code>@​remcohaszing</code></a> in <a href="https://redirect.github.com/remarkjs/react-markdown/pull/893">remarkjs/react-markdown#893</a></li>
</ul>
<p><strong>Full Changelog</strong>: <a href="https://github.com/remarkjs/react-markdown/compare/10.0.0...10.0.1">https://github.com/remarkjs/react-markdown/compare/10.0.0...10.0.1</a></p>
<h2>10.0.0</h2>
<ul>
<li>aaaa40b Remove support for <code>className</code> prop
see <a href="https://github.com/remarkjs/react-markdown/blob/main/changelog.md#remove-classname">“Remove className”</a></li>
</ul>
<p><strong>Full Changelog</strong>: <a href="https://github.com/remarkjs/react-markdown/compare/9.1.0...10.0.0">https://github.com/remarkjs/react-markdown/compare/9.1.0...10.0.0</a></p>
</blockquote>
</details>
<details>
<summary>Changelog</summary>
<p><em>Sourced from <a href="https://github.com/remarkjs/react-markdown/blob/main/changelog.md">react-markdown's changelog</a>.</em></p>
<blockquote>
<!-- raw HTML omitted -->
<h1>Changelog</h1>
<p>All notable changes will be documented in this file.</p>
<h2>10.0.0 - 2025-02-20</h2>
<ul>
<li><a href="https://github.com/remarkjs/react-markdown/commit/aaaa40b"><code>aaaa40b</code></a>
Remove support for <code>className</code> prop
<strong>migrate</strong>: see “Remove <code>className</code>” below</li>
</ul>
<h3>Remove <code>className</code></h3>
<p>The <code>className</code> prop was removed.
If you want to add classes to some element that wraps the markdown
you can explicitly write that element and add the class to it.
You can then choose yourself which tag name to use and whether to add other
props.</p>
<p>Before:</p>
<pre lang="js"><code>&lt;Markdown className=&quot;markdown-body&quot;&gt;{markdown}&lt;/Markdown&gt;
</code></pre>
<p>After:</p>
<pre lang="js"><code>&lt;div className=&quot;markdown-body&quot;&gt;
  &lt;Markdown&gt;{markdown}&lt;/Markdown&gt;
&lt;/div&gt;
</code></pre>
</blockquote>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/remarkjs/react-markdown/commit/44d2e4a44b37461ab7778d6870c1a9eb36393ad2"><code>44d2e4a</code></a> 10.1.0</li>
<li><a href="https://github.com/remarkjs/react-markdown/commit/f2369cd7b7f3c8eb01b7ba1221cf305b7474716f"><code>f2369cd</code></a> Refactor docs</li>
<li><a href="https://github.com/remarkjs/react-markdown/commit/26fdfe037516f9eee7e4c9472d633b795acc53e5"><code>26fdfe0</code></a> Update docs</li>
<li><a href="https://github.com/remarkjs/react-markdown/commit/544bff69fbd406b397bed3bc411f7bb12ad82b08"><code>544bff6</code></a> Refactor code-style</li>
<li><a href="https://github.com/remarkjs/react-markdown/commit/939c6671c9dbffccfe8e27bba256f62405031193"><code>939c667</code></a> Add <code>fallback</code> prop to <code>MarkdownHooks</code></li>
<li><a href="https://github.com/remarkjs/react-markdown/commit/a40ae2e3131eca0421c43bc179b63f05be0bfbb9"><code>a40ae2e</code></a> Fix race condition in <code>MarkdownHooks</code></li>
<li><a href="https://github.com/remarkjs/react-markdown/commit/ad7f37f0b407ed90663e0ff85dda246f7987b5a9"><code>ad7f37f</code></a> Add lifecycle tests for <code>MarkdownHooks</code></li>
<li><a href="https://github.com/remarkjs/react-markdown/commit/2792c32cdd2e7fd38e5d79fe5761da521d3ca0ae"><code>2792c32</code></a> 10.0.1</li>
<li><a href="https://github.com/remarkjs/react-markdown/commit/7c17ede8e47f57785d0b82a7b42fffd8287bf3a3"><code>7c17ede</code></a> Fix performance around components</li>
<li><a href="https://github.com/remarkjs/react-markdown/commit/21b47b9e7f916602987e1b85e7df7a688b9957ee"><code>21b47b9</code></a> Remove local use of <code>JSX</code></li>
<li>Additional commits viewable in <a href="https://github.com/remarkjs/react-markdown/compare/9.1.0...10.1.0">compare view</a></li>
</ul>
</details>
<br />

<details><summary>Comment — nathanpond, 2026-08-31</summary>

@dependabot rebase

</details>

---

## archived-111 — Bump mantine-datatable from 8.3.13 to 9.4.0 in /src/AutoNate.Spa

`MERGED (merged 2026-08-31)` · app/dependabot · opened 2026-08-31 · `dependabot/npm_and_yarn/src/AutoNate.Spa/mantine-datatable-9.4.0` → `master`

Bumps [mantine-datatable](https://github.com/icflorescu/mantine-datatable) from 8.3.13 to 9.4.0.
<details>
<summary>Changelog</summary>
<p><em>Sourced from <a href="https://github.com/icflorescu/mantine-datatable/blob/main/CHANGELOG.md">mantine-datatable's changelog</a>.</em></p>
<blockquote>
<h2>9.4.0 (2026-07-15)</h2>
<ul>
<li>Update dev deps to ensure compatibility with Mantine 9.4.0</li>
</ul>
<h2>9.3.1 (2026-06-19)</h2>
<ul>
<li>Update dev deps to ensure compatibility with Mantine 9.3.1</li>
<li>Update GitHub workflow action and Node.js versions</li>
<li>Update Biome version and config</li>
<li>Target dependabot PRs against the <code>next</code> branch</li>
</ul>
<h2>9.3.0 (2026-06-12)</h2>
<ul>
<li>Fix issue <a href="https://redirect.github.com/icflorescu/mantine-datatable/issues/818">#818</a> - DataTable crashes when invalid persisted column state is received via localStorage/storage events, thanks to <a href="https://github.com/main03">Sheharyar Khalid</a> for raising and fixing</li>
<li>Update dev deps and ensure compatibility with Mantine 9.3</li>
<li>Harden npm publishing workflow security</li>
</ul>
<h2>9.2.2 (2026-05-18)</h2>
<ul>
<li>Fix issue <a href="https://redirect.github.com/icflorescu/mantine-datatable/issues/743">#743</a> - abnormal vertical scroll bar When using both footer and selection at the same time</li>
</ul>
<h2>9.2.1 (2026-05-18)</h2>
<ul>
<li>Use fixed deps in dev dependencies</li>
<li>Update deps to ensure compatibility with Mantine 9.2.1</li>
<li>Increase scoll area elements z-index (fixes issues like <a href="https://redirect.github.com/icflorescu/mantine-datatable/issues/808">#808</a>)</li>
<li>Fix issue <a href="https://redirect.github.com/icflorescu/mantine-datatable/issues/790">#790</a></li>
<li>Export type definition for <code>DataTablePaginationRenderContext</code> (feature request <a href="https://redirect.github.com/icflorescu/mantine-datatable/issues/772">#772</a>)</li>
</ul>
<h2>9.2.0 (2026-05-13)</h2>
<ul>
<li>Migrate to Mantine v9 and Next.js 16, thanks to <a href="https://github.com/pfo-omicsstudio">pfo-omicsstudio</a> for [PR <a href="https://redirect.github.com/icflorescu/mantine-datatable/issues/804">#804</a>](<a href="https://redirect.github.com/icflorescu/mantine-datatable/pull/804">icflorescu/mantine-datatable#804</a>)</li>
<li>Implement arbitrary column pinning, thanks to <a href="https://github.com/DavidTanner">DavidTanner</a> for [PR <a href="https://redirect.github.com/icflorescu/mantine-datatable/issues/794">#794</a>](<a href="https://redirect.github.com/icflorescu/mantine-datatable/pull/794">icflorescu/mantine-datatable#794</a>)</li>
<li>Fix <a href="https://redirect.github.com/icflorescu/mantine-datatable/issues/789">#789</a> - rewrite column resize to honor declarative widths, thanks to <a href="https://github.com/gfazioli">Giovambattista Fazioli</a> for [PR <a href="https://redirect.github.com/icflorescu/mantine-datatable/issues/803">#803</a>](<a href="https://redirect.github.com/icflorescu/mantine-datatable/pull/803">icflorescu/mantine-datatable#803</a>)</li>
<li>Implement <code>onDismiss</code> handler to handle Escape button click in filters, thanks to <a href="https://github.com/DavidTanner">DavidTanner</a> for [PR <a href="https://redirect.github.com/icflorescu/mantine-datatable/issues/796">#796</a>](<a href="https://redirect.github.com/icflorescu/mantine-datatable/pull/796">icflorescu/mantine-datatable#796</a>)</li>
<li>Switch linting &amp; formatting to Biome</li>
<li>Update to TypeScript 6</li>
</ul>
</blockquote>
</details>
<details>
<summary>Commits</summary>
<ul>
<li>See full diff in <a href="https://github.com/icflorescu/mantine-datatable/commits">compare view</a></li>
</ul>
</details>
<details>
<summary>Maintainer changes</summary>
<p>This version was pushed to npm by <a href="https://www.npmjs.com/~GitHub%20Actions">GitHub Actions</a>, a new releaser for mantine-datatable since your current version.</p>
</details>
<br />

<details><summary>Comment — nathanpond, 2026-08-31</summary>

@dependabot rebase

</details>

---

## archived-127 — Bump postcss from 8.5.10 to 8.5.26 in /src/AutoNate.Spa

`MERGED (merged 2026-08-31)` · app/dependabot · opened 2026-08-31 · `dependabot/npm_and_yarn/src/AutoNate.Spa/postcss-8.5.26` → `master`

Bumps [postcss](https://github.com/postcss/postcss) from 8.5.10 to 8.5.26.
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/postcss/postcss/releases">postcss's releases</a>.</em></p>
<blockquote>
<h2>8.5.26</h2>
<ul>
<li>Fixed <code>list.split()</code> regression (by <a href="https://github.com/lazerg"><code>@​lazerg</code></a>).</li>
<li>Track symlinks in path protection in source map loading (by <a href="https://github.com/drengir1"><code>@​drengir1</code></a>).</li>
</ul>
<h2>8.5.25</h2>
<ul>
<li>Fixed 8.5.17 visitor regression.</li>
<li>Fixed <code>list.split()</code> for non-string values (by <a href="https://github.com/amir-rezaei"><code>@​amir-rezaei</code></a>).</li>
</ul>
<h2>8.5.24</h2>
<ul>
<li>Preserve the BOM after the processing (by <a href="https://github.com/hdimer"><code>@​hdimer</code></a>).</li>
</ul>
<h2>8.5.23</h2>
<ul>
<li>Do not load source map without <code>opts.from</code> for security reasons.</li>
</ul>
<h2>8.5.22</h2>
<ul>
<li>Fixed custom property losing semicolon before a comment (by <a href="https://github.com/sarathfrancis90"><code>@​sarathfrancis90</code></a>).</li>
</ul>
<h2>8.5.21</h2>
<ul>
<li>Fixed childless at-rule losing semicolon before comment (by <a href="https://github.com/sarathfrancis90"><code>@​sarathfrancis90</code></a>).</li>
<li>Fixed docs (by <a href="https://github.com/isker"><code>@​isker</code></a>).</li>
</ul>
<h2>8.5.20</h2>
<ul>
<li>Fixed missing space if <code>AtRule#params</code> is set after (by <a href="https://github.com/sarathfrancis90"><code>@​sarathfrancis90</code></a>).</li>
<li>Fixed mixing AST error on warnings (by <a href="https://github.com/MahinAnowar"><code>@​MahinAnowar</code></a>).</li>
</ul>
<h2>8.5.19</h2>
<ul>
<li>Fixed cleaning <code>before</code> for new nodes inserted to <code>Root</code> (by <a href="https://github.com/MahinAnowar"><code>@​MahinAnowar</code></a>).</li>
</ul>
<h2>8.5.18</h2>
<ul>
<li>Restricted loading previous source maps file to the <code>opts.from</code> folder for security reasons (use <code>unsafeMap: true</code> to disable the check).</li>
</ul>
<h2>8.5.17</h2>
<ul>
<li>Fixed <code>Maximum call stack size exceeded</code> error.</li>
<li>Fixed Prototype hijacking for <code>postcss.fromJSON()</code>.</li>
<li>Fixed <code>Input#origin()</code> for unmapped end position (by <a href="https://github.com/chatman-media"><code>@​chatman-media</code></a>).</li>
</ul>
<h2>8.5.16</h2>
<ul>
<li>Fixed <code>Input#origin()</code> position (by <a href="https://github.com/mizdra"><code>@​mizdra</code></a>).</li>
<li>Fixed <code>raws</code> after rehydrating a JSON AST (by <a href="https://github.com/sarathfrancis90"><code>@​sarathfrancis90</code></a>).</li>
<li>Fixed putting parent-less node in <code>nodes</code> of new node (by <a href="https://github.com/MahinAnowar"><code>@​MahinAnowar</code></a>).</li>
<li>Fixed computing <code>offset</code> in <code>positionBy()</code> (by <a href="https://github.com/greymoth-jp"><code>@​greymoth-jp</code></a>).</li>
<li>Fixed <code>rangeBy()</code> on <code>index: 0</code> (by <a href="https://github.com/sarathfrancis90"><code>@​sarathfrancis90</code></a>).</li>
</ul>
<h2>8.5.15</h2>
<ul>
<li>Fixed declaration parsing performance (by <a href="https://github.com/homanp"><code>@​homanp</code></a>).</li>
</ul>
<h2>8.5.14</h2>
<ul>
<li>Fixed custom syntax regression (by <a href="https://github.com/43081j"><code>@​43081j</code></a>).</li>
</ul>
<h2>8.5.13</h2>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Changelog</summary>
<p><em>Sourced from <a href="https://github.com/postcss/postcss/blob/main/CHANGELOG.md">postcss's changelog</a>.</em></p>
<blockquote>
<h2>8.5.26</h2>
<ul>
<li>Fixed <code>list.split()</code> regression (by <a href="https://github.com/lazerg"><code>@​lazerg</code></a>).</li>
<li>Track symlinks in path protection in source map loading (by <a href="https://github.com/drengir1"><code>@​drengir1</code></a>).</li>
</ul>
<h2>8.5.25</h2>
<ul>
<li>Fixed 8.5.17 visitor regression.</li>
<li>Fixed <code>list.split()</code> for non-string values (by <a href="https://github.com/amir-rezaei"><code>@​amir-rezaei</code></a>).</li>
</ul>
<h2>8.5.24</h2>
<ul>
<li>Preserve the BOM after the processing (by <a href="https://github.com/hdimer"><code>@​hdimer</code></a>).</li>
</ul>
<h2>8.5.23</h2>
<ul>
<li>Do not load source map without <code>opts.from</code> for security reasons.</li>
</ul>
<h2>8.5.22</h2>
<ul>
<li>Fixed custom property losing semicolon before a comment (by <a href="https://github.com/sarathfrancis90"><code>@​sarathfrancis90</code></a>).</li>
</ul>
<h2>8.5.21</h2>
<ul>
<li>Fixed childless at-rule losing semicolon before comment (by <a href="https://github.com/sarathfrancis90"><code>@​sarathfrancis90</code></a>).</li>
<li>Fixed docs (by <a href="https://github.com/isker"><code>@​isker</code></a>).</li>
</ul>
<h2>8.5.20</h2>
<ul>
<li>Fixed missing space if <code>AtRule#params</code> is set after (by <a href="https://github.com/sarathfrancis90"><code>@​sarathfrancis90</code></a>).</li>
<li>Fixed mixing AST error on warnings (by <a href="https://github.com/MahinAnowar"><code>@​MahinAnowar</code></a>).</li>
</ul>
<h2>8.5.19</h2>
<ul>
<li>Fixed cleaning <code>before</code> for new nodes inserted to <code>Root</code> (by <a href="https://github.com/MahinAnowar"><code>@​MahinAnowar</code></a>).</li>
</ul>
<h2>8.5.18</h2>
<ul>
<li>Restricted loading previous source maps file to the <code>opts.from</code> folder for security reasons (use <code>unsafeMap: true</code> to disable the check).</li>
</ul>
<h2>8.5.17</h2>
<ul>
<li>Fixed <code>Maximum call stack size exceeded</code> error.</li>
<li>Fixed Prototype hijacking for <code>postcss.fromJSON()</code>.</li>
<li>Fixed <code>Input#origin()</code> for unmapped end position (by <a href="https://github.com/chatman-media"><code>@​chatman-media</code></a>).</li>
</ul>
<h2>8.5.16</h2>
<ul>
<li>Fixed <code>Input#origin()</code> position (by <a href="https://github.com/mizdra"><code>@​mizdra</code></a>).</li>
<li>Fixed <code>raws</code> after rehydrating a JSON AST (by <a href="https://github.com/sarathfrancis90"><code>@​sarathfrancis90</code></a>).</li>
</ul>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/postcss/postcss/commit/07b25773f38f77919f2af02ae3e8896b0deb5988"><code>07b2577</code></a> Release 8.5.26 version</li>
<li><a href="https://github.com/postcss/postcss/commit/47de6b9d7c55674cb326c5de7a734a740916defc"><code>47de6b9</code></a> Update CI</li>
<li><a href="https://github.com/postcss/postcss/commit/1493a83db7830912316512f55ab6064e7b7dd68e"><code>1493a83</code></a> Fix Rule#selectors losing the empty selector (<a href="https://redirect.github.com/postcss/postcss/issues/2129">#2129</a>)</li>
<li><a href="https://github.com/postcss/postcss/commit/180db166e250d20e6761b224ae8d8134c9ba3e40"><code>180db16</code></a> Typo</li>
<li><a href="https://github.com/postcss/postcss/commit/29e9e00f132c96e46e1de295b816fe88a05354e7"><code>29e9e00</code></a> Resolve symlinks before the previous-source-map containment check (<a href="https://redirect.github.com/postcss/postcss/issues/2125">#2125</a>)</li>
<li><a href="https://github.com/postcss/postcss/commit/3ba8f84703a884329b58abea579c3615684e0b7e"><code>3ba8f84</code></a> Update dependencies</li>
<li><a href="https://github.com/postcss/postcss/commit/87e72f671fd0d401c52822b5226c656632d92ec0"><code>87e72f6</code></a> Update lock file</li>
<li><a href="https://github.com/postcss/postcss/commit/caaeeb907e4a816c44a23b00b151882bd02325a1"><code>caaeeb9</code></a> Upgrade nanoid to fix infinite loop on zero size (<a href="https://redirect.github.com/postcss/postcss/issues/2124">#2124</a>)</li>
<li><a href="https://github.com/postcss/postcss/commit/3609b6f4296952d0b5b9ddae42c8d73ee460c041"><code>3609b6f</code></a> Explain how to type plugin options</li>
<li><a href="https://github.com/postcss/postcss/commit/fbad419cbd01cd7a9a1a46413447f2cd9b3fce4a"><code>fbad419</code></a> docs: show ESM and TypeScript plugin declaration (<a href="https://redirect.github.com/postcss/postcss/issues/2118">#2118</a>)</li>
<li>Additional commits viewable in <a href="https://github.com/postcss/postcss/compare/8.5.10...8.5.26">compare view</a></li>
</ul>
</details>
<details>
<summary>Maintainer changes</summary>
<p>This version was pushed to npm by <a href="https://www.npmjs.com/~GitHub%20Actions">GitHub Actions</a>, a new releaser for postcss since your current version.</p>
</details>
<br />


[![Dependabot compatibility score](https://dependabot-badges.githubapp.com/badges/compatibility_score?dependency-name=postcss&package-manager=npm_and_yarn&previous-version=8.5.10&new-version=8.5.26)](https://docs.github.com/en/github/managing-security-vulnerabilities/about-dependabot-security-updates#about-compatibility-scores)

Dependabot will resolve any conflicts with this PR as long as you don't alter it yourself. You can also trigger a rebase manually by commenting `@dependabot rebase`.

[//]: # (dependabot-automerge-start)
[//]: # (dependabot-automerge-end)

---

<details>
<summary>Dependabot commands and options</summary>
<br />

You can trigger Dependabot actions by commenting on this PR:
- `@dependabot rebase` will rebase this PR
- `@dependabot recreate` will recreate this PR, overwriting any edits that have been made to it
- `@dependabot show <dependency name> ignore conditions` will show all of the ignore conditions of the specified dependency
- `@dependabot ignore this major version` will close this PR and stop Dependabot creating any more for this major version (unless you reopen the PR or upgrade to it yourself)
- `@dependabot ignore this minor version` will close this PR and stop Dependabot creating any more for this minor version (unless you reopen the PR or upgrade to it yourself)
- `@dependabot ignore this dependency` will close this PR and stop Dependabot creating any more for this dependency (unless you reopen the PR or upgrade to it yourself)
You can disable automated security fix PRs for this repo from the [Security Alerts page](https://github.com/nathanpond/AutoNate/network/alerts).

</details>

---

## archived-128 — Bump brace-expansion in /src/AutoNate.Spa

`MERGED (merged 2026-08-31)` · app/dependabot · opened 2026-08-31 · `dependabot/npm_and_yarn/src/AutoNate.Spa/multi-5e81c1b34f` → `master`

Bumps  and [brace-expansion](https://github.com/juliangruber/brace-expansion). These dependencies needed to be updated together.
Updates `brace-expansion` from 1.1.15 to 1.1.18
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/juliangruber/brace-expansion/commit/758fcd6d188a95c2342818519c77b8c06794552b"><code>758fcd6</code></a> 1.1.18</li>
<li><a href="https://github.com/juliangruber/brace-expansion/commit/27fbeed22b4fdf2c5f732f66bcf84d43f4a26c6e"><code>27fbeed</code></a> Merge commit from fork</li>
<li><a href="https://github.com/juliangruber/brace-expansion/commit/5c57cc2519dfb067e188b7cb0733fffbd02946bf"><code>5c57cc2</code></a> 1.1.17</li>
<li><a href="https://github.com/juliangruber/brace-expansion/commit/d757f1dde7808bcbcd7a4628ab913e5185ed3d57"><code>d757f1d</code></a> npm ignore <code>.claude</code></li>
<li><a href="https://github.com/juliangruber/brace-expansion/commit/cb4b9e47cc2ec777c14b2b4492fb431a56f6a031"><code>cb4b9e4</code></a> fix: backport GHSA-mh99-v99m-4gvg (<a href="https://redirect.github.com/juliangruber/brace-expansion/issues/129">#129</a>)</li>
<li><a href="https://github.com/juliangruber/brace-expansion/commit/447763a91a613cfa67ac73096cbc1de9a2304f97"><code>447763a</code></a> 1.1.16</li>
<li><a href="https://github.com/juliangruber/brace-expansion/commit/d74e63030c012e3b7ae81657b8d665619cd51b95"><code>d74e630</code></a> fix: v1 backport for CVE-2026-13149 (<a href="https://redirect.github.com/juliangruber/brace-expansion/issues/122">#122</a>)</li>
<li>See full diff in <a href="https://github.com/juliangruber/brace-expansion/compare/v1.1.15...v1.1.18">compare view</a></li>
</ul>
</details>
<br />

Updates `brace-expansion` from 5.0.6 to 5.0.9
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/juliangruber/brace-expansion/commit/758fcd6d188a95c2342818519c77b8c06794552b"><code>758fcd6</code></a> 1.1.18</li>
<li><a href="https://github.com/juliangruber/brace-expansion/commit/27fbeed22b4fdf2c5f732f66bcf84d43f4a26c6e"><code>27fbeed</code></a> Merge commit from fork</li>
<li><a href="https://github.com/juliangruber/brace-expansion/commit/5c57cc2519dfb067e188b7cb0733fffbd02946bf"><code>5c57cc2</code></a> 1.1.17</li>
<li><a href="https://github.com/juliangruber/brace-expansion/commit/d757f1dde7808bcbcd7a4628ab913e5185ed3d57"><code>d757f1d</code></a> npm ignore <code>.claude</code></li>
<li><a href="https://github.com/juliangruber/brace-expansion/commit/cb4b9e47cc2ec777c14b2b4492fb431a56f6a031"><code>cb4b9e4</code></a> fix: backport GHSA-mh99-v99m-4gvg (<a href="https://redirect.github.com/juliangruber/brace-expansion/issues/129">#129</a>)</li>
<li><a href="https://github.com/juliangruber/brace-expansion/commit/447763a91a613cfa67ac73096cbc1de9a2304f97"><code>447763a</code></a> 1.1.16</li>
<li><a href="https://github.com/juliangruber/brace-expansion/commit/d74e63030c012e3b7ae81657b8d665619cd51b95"><code>d74e630</code></a> fix: v1 backport for CVE-2026-13149 (<a href="https://redirect.github.com/juliangruber/brace-expansion/issues/122">#122</a>)</li>
<li>See full diff in <a href="https://github.com/juliangruber/brace-expansion/compare/v1.1.15...v1.1.18">compare view</a></li>
</ul>
</details>
<br />


Dependabot will resolve any conflicts with this PR as long as you don't alter it yourself. You can also trigger a rebase manually by commenting `@dependabot rebase`.

[//]: # (dependabot-automerge-start)
[//]: # (dependabot-automerge-end)

---

<details>
<summary>Dependabot commands and options</summary>
<br />

You can trigger Dependabot actions by commenting on this PR:
- `@dependabot rebase` will rebase this PR
- `@dependabot recreate` will recreate this PR, overwriting any edits that have been made to it
- `@dependabot show <dependency name> ignore conditions` will show all of the ignore conditions of the specified dependency
- `@dependabot ignore this major version` will close this PR and stop Dependabot creating any more for this major version (unless you reopen the PR or upgrade to it yourself)
- `@dependabot ignore this minor version` will close this PR and stop Dependabot creating any more for this minor version (unless you reopen the PR or upgrade to it yourself)
- `@dependabot ignore this dependency` will close this PR and stop Dependabot creating any more for this dependency (unless you reopen the PR or upgrade to it yourself)
You can disable automated security fix PRs for this repo from the [Security Alerts page](https://github.com/nathanpond/AutoNate/network/alerts).

</details>

---

## archived-129 — Bump axios from 1.15.2 to 1.18.0 in /src/AutoNate.Spa

`MERGED (merged 2026-08-31)` · app/dependabot · opened 2026-08-31 · `dependabot/npm_and_yarn/src/AutoNate.Spa/axios-1.18.0` → `master`

Bumps [axios](https://github.com/axios/axios) from 1.15.2 to 1.18.0.
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/axios/axios/releases">axios's releases</a>.</em></p>
<blockquote>
<h2>v1.18.0 — June 13, 2026</h2>
<p>This release hardens redirect and URL handling, improves the validateStatus configuration semantics, and includes updates to documentation, dependencies, and release metadata.</p>
<h2>🔒 Security Fixes</h2>
<ul>
<li>
<p><strong>Redirect Header Safety:</strong> Added Node HTTP adapter support for stripping caller-specified sensitive headers on cross-origin redirects, helping prevent custom auth headers such as API keys from leaking to another origin. (<strong><a href="https://redirect.github.com/axios/axios/issues/10892">#10892</a></strong>)</p>
</li>
<li>
<p><strong>URL And Request Hardening:</strong> Rejects malformed <code>http:</code> and <code>https:</code> URLs that omit <code>//</code> with <code>ERR_INVALID_URL</code>, while tightening prototype-pollution-safe config reads, stream size limits, FormData depth handling, data URL sizing, and local <code>NO_PROXY</code> matching. (<strong><a href="https://redirect.github.com/axios/axios/issues/11000">#11000</a></strong>)</p>
</li>
</ul>
<h2>🐛 Bug Fixes</h2>
<ul>
<li><strong>Status Validation:</strong> Added <code>transitional.validateStatusUndefinedResolves</code> so applications can opt in to treating <code>validateStatus: undefined</code> like the option was omitted, while <code>validateStatus: null</code> remains the explicit way to accept every status. (<strong><a href="https://redirect.github.com/axios/axios/issues/10899">#10899</a></strong>)</li>
</ul>
<h2>🔧 Maintenance &amp; Chores</h2>
<ul>
<li>
<p><strong>Documentation:</strong> Published the v1.17.0 release notes, fixed a changelog typo, clarified the package update PR policy, and marked the <code>proxy</code> request config as Node.js-only in the advanced docs. (<strong><a href="https://redirect.github.com/axios/axios/issues/10984">#10984</a></strong>, <strong><a href="https://redirect.github.com/axios/axios/issues/10988">#10988</a></strong>, <strong><a href="https://redirect.github.com/axios/axios/issues/10992">#10992</a></strong>, <strong><a href="https://redirect.github.com/axios/axios/issues/10995">#10995</a></strong>)</p>
</li>
<li>
<p><strong>Dependencies:</strong> Bumped <code>@babel/core</code>, <code>@babel/preset-env</code>, <code>@commitlint/cli</code>, <code>@commitlint/config-conventional</code>, <code>@rollup/plugin-babel</code>, <code>@rollup/plugin-commonjs</code>, <code>@vitest/browser</code>, <code>@vitest/browser-playwright</code>, <code>eslint</code>, <code>lint-staged</code>, <code>rollup</code>, <code>vitest</code>, and <code>actions/checkout</code>. (<strong><a href="https://redirect.github.com/axios/axios/issues/10989">#10989</a></strong>, <strong><a href="https://redirect.github.com/axios/axios/issues/10996">#10996</a></strong>, <strong><a href="https://redirect.github.com/axios/axios/issues/10997">#10997</a></strong>)</p>
</li>
<li>
<p><strong>Release Metadata:</strong> Prepared the 1.18.0 release by updating package metadata and the runtime <code>VERSION</code> value. (<strong><a href="https://redirect.github.com/axios/axios/issues/11003">#11003</a></strong>)</p>
</li>
</ul>
<h2>🌟 New Contributors</h2>
<p>We are thrilled to welcome our new contributors. Thank you for helping improve axios:</p>
<ul>
<li><strong><a href="https://github.com/drori12"><code>@​drori12</code></a></strong> (<strong><a href="https://redirect.github.com/axios/axios/issues/10984">#10984</a></strong>)</li>
<li><strong><a href="https://github.com/eyupcanakman"><code>@​eyupcanakman</code></a></strong> (<strong><a href="https://redirect.github.com/axios/axios/issues/10899">#10899</a></strong>)</li>
<li><strong><a href="https://github.com/Adi-Beker"><code>@​Adi-Beker</code></a></strong> (<strong><a href="https://redirect.github.com/axios/axios/issues/10995">#10995</a></strong>)</li>
</ul>
<p><a href="https://github.com/axios/axios/compare/v1.17.0...v1.18.0">Full Changelog</a></p>
<h2>v1.17.0 — June 1, 2026</h2>
<p>This release adds Node HTTP zstd decompression, hardens config and release workflows, and fixes authentication, header, proxy, and type-handling regressions.</p>
<h2>🔒 Security Fixes</h2>
<ul>
<li><strong>Config Hardening:</strong> Guarded <code>socketPath</code>, <code>params</code>, and <code>paramsSerializer</code> reads with own-property checks to prevent inherited prototype values from affecting request behavior, including SSRF-sensitive paths. (<strong><a href="https://redirect.github.com/axios/axios/issues/10901">#10901</a></strong>, <strong><a href="https://redirect.github.com/axios/axios/issues/10922">#10922</a></strong>)</li>
<li><strong>Release Publishing:</strong> Switched the publish workflow to npm staged publishing for safer, auditable package releases with provenance. (<strong><a href="https://redirect.github.com/axios/axios/issues/10926">#10926</a></strong>)</li>
</ul>
<h2>🚀 New Features</h2>
<ul>
<li><strong>HTTP Compression:</strong> Added Node HTTP adapter support for zstd response decompression, with <code>transitional.advertiseZstdAcceptEncoding</code> controlling whether <code>zstd</code> is advertised in <code>Accept-Encoding</code>. (<strong><a href="https://redirect.github.com/axios/axios/issues/6792">#6792</a></strong>, <strong><a href="https://redirect.github.com/axios/axios/issues/10920">#10920</a></strong>)</li>
</ul>
<h2>🐛 Bug Fixes</h2>
<ul>
<li><strong>Authentication Handling:</strong> Restored Basic auth on same-origin Node redirects while continuing to strip credentials cross-origin, and aligned the fetch adapter with HTTP adapter behavior for URL-embedded Basic auth. (<strong><a href="https://redirect.github.com/axios/axios/issues/10929">#10929</a></strong>, <strong><a href="https://redirect.github.com/axios/axios/issues/10896">#10896</a></strong>)</li>
<li><strong>Proxy TLS:</strong> Preserved user <code>httpsAgent</code> TLS options when tunneling HTTPS requests through HTTP CONNECT proxies. (<strong><a href="https://redirect.github.com/axios/axios/issues/10957">#10957</a></strong>)</li>
<li><strong>React Native FormData:</strong> Cleared default <code>Content-Type</code> for React Native <code>FormData</code> so multipart boundaries can be generated correctly. (<strong><a href="https://redirect.github.com/axios/axios/issues/10898">#10898</a></strong>)</li>
</ul>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Changelog</summary>
<p><em>Sourced from <a href="https://github.com/axios/axios/blob/v1.x/CHANGELOG.md">axios's changelog</a>.</em></p>
<blockquote>
<h2>v1.18.0 — June 13, 2026</h2>
<p>This release hardens redirect and URL handling, improves the validateStatus configuration semantics, and includes updates to documentation, dependencies, and release metadata.</p>
<h2>🔒 Security Fixes</h2>
<ul>
<li>
<p><strong>Redirect Header Safety:</strong> Added Node HTTP adapter support for stripping caller-specified sensitive headers on cross-origin redirects, helping prevent custom auth headers such as API keys from leaking to another origin. (<strong><a href="https://redirect.github.com/axios/axios/issues/10892">#10892</a></strong>)</p>
</li>
<li>
<p><strong>URL And Request Hardening:</strong> Rejects malformed <code>http:</code> and <code>https:</code> URLs that omit <code>//</code> with <code>ERR_INVALID_URL</code>, while tightening prototype-pollution-safe config reads, stream size limits, FormData depth handling, data URL sizing, and local <code>NO_PROXY</code> matching. (<strong><a href="https://redirect.github.com/axios/axios/issues/11000">#11000</a></strong>)</p>
</li>
</ul>
<h2>🐛 Bug Fixes</h2>
<ul>
<li><strong>Status Validation:</strong> Added <code>transitional.validateStatusUndefinedResolves</code> so applications can opt in to treating <code>validateStatus: undefined</code> like the option was omitted, while <code>validateStatus: null</code> remains the explicit way to accept every status. (<strong><a href="https://redirect.github.com/axios/axios/issues/10899">#10899</a></strong>)</li>
</ul>
<h2>🔧 Maintenance &amp; Chores</h2>
<ul>
<li>
<p><strong>Documentation:</strong> Published the v1.17.0 release notes, fixed a changelog typo, clarified the package update PR policy, and marked the <code>proxy</code> request config as Node.js-only in the advanced docs. (<strong><a href="https://redirect.github.com/axios/axios/issues/10984">#10984</a></strong>, <strong><a href="https://redirect.github.com/axios/axios/issues/10988">#10988</a></strong>, <strong><a href="https://redirect.github.com/axios/axios/issues/10992">#10992</a></strong>, <strong><a href="https://redirect.github.com/axios/axios/issues/10995">#10995</a></strong>)</p>
</li>
<li>
<p><strong>Dependencies:</strong> Bumped <code>@babel/core</code>, <code>@babel/preset-env</code>, <code>@commitlint/cli</code>, <code>@commitlint/config-conventional</code>, <code>@rollup/plugin-babel</code>, <code>@rollup/plugin-commonjs</code>, <code>@vitest/browser</code>, <code>@vitest/browser-playwright</code>, <code>eslint</code>, <code>lint-staged</code>, <code>rollup</code>, <code>vitest</code>, and <code>actions/checkout</code>. (<strong><a href="https://redirect.github.com/axios/axios/issues/10989">#10989</a></strong>, <strong><a href="https://redirect.github.com/axios/axios/issues/10996">#10996</a></strong>, <strong><a href="https://redirect.github.com/axios/axios/issues/10997">#10997</a></strong>)</p>
</li>
<li>
<p><strong>Release Metadata:</strong> Prepared the 1.18.0 release by updating package metadata and the runtime <code>VERSION</code> value. (<strong><a href="https://redirect.github.com/axios/axios/issues/11003">#11003</a></strong>)</p>
</li>
</ul>
<h2>🌟 New Contributors</h2>
<p>We are thrilled to welcome our new contributors. Thank you for helping improve axios:</p>
<ul>
<li><strong><a href="https://github.com/drori12"><code>@​drori12</code></a></strong> (<strong><a href="https://redirect.github.com/axios/axios/issues/10984">#10984</a></strong>)</li>
<li><strong><a href="https://github.com/eyupcanakman"><code>@​eyupcanakman</code></a></strong> (<strong><a href="https://redirect.github.com/axios/axios/issues/10899">#10899</a></strong>)</li>
<li><strong><a href="https://github.com/Adi-Beker"><code>@​Adi-Beker</code></a></strong> (<strong><a href="https://redirect.github.com/axios/axios/issues/10995">#10995</a></strong>)</li>
</ul>
<p><a href="https://github.com/axios/axios/compare/v1.17.0...v1.18.0">Full Changelog</a></p>
<h2>v1.17.0 — June 1, 2026</h2>
<p>This release adds Node HTTP zstd decompression, hardens config and release workflows, and fixes authentication, header, proxy, and type-handling regressions.</p>
<h2>🔒 Security Fixes</h2>
<ul>
<li><strong>Config Hardening:</strong> Guarded <code>socketPath</code>, <code>params</code>, and <code>paramsSerializer</code> reads with own-property checks to prevent inherited prototype values from affecting request behavior, including SSRF-sensitive paths. (<strong><a href="https://redirect.github.com/axios/axios/issues/10901">#10901</a></strong>, <strong><a href="https://redirect.github.com/axios/axios/issues/10922">#10922</a></strong>)</li>
<li><strong>Release Publishing:</strong> Switched the publish workflow to npm staged publishing for safer, auditable package releases with provenance. (<strong><a href="https://redirect.github.com/axios/axios/issues/10926">#10926</a></strong>)</li>
</ul>
<h2>🚀 New Features</h2>
<ul>
<li><strong>HTTP Compression:</strong> Added Node HTTP adapter support for zstd response decompression, with <code>transitional.advertiseZstdAcceptEncoding</code> controlling whether <code>zstd</code> is advertised in <code>Accept-Encoding</code>. (<strong><a href="https://redirect.github.com/axios/axios/issues/6792">#6792</a></strong>, <strong><a href="https://redirect.github.com/axios/axios/issues/10920">#10920</a></strong>)</li>
</ul>
<h2>🐛 Bug Fixes</h2>
<ul>
<li><strong>Authentication Handling:</strong> Restored Basic auth on same-origin Node redirects while continuing to strip credentials cross-origin, and aligned the fetch adapter with HTTP adapter behavior for URL-embedded Basic auth. (<strong><a href="https://redirect.github.com/axios/axios/issues/10929">#10929</a></strong>, <strong><a href="https://redirect.github.com/axios/axios/issues/10896">#10896</a></strong>)</li>
<li><strong>Proxy TLS:</strong> Preserved user <code>httpsAgent</code> TLS options when tunneling HTTPS requests through HTTP CONNECT proxies. (<strong><a href="https://redirect.github.com/axios/axios/issues/10957">#10957</a></strong>)</li>
<li><strong>React Native FormData:</strong> Cleared default <code>Content-Type</code> for React Native <code>FormData</code> so multipart boundaries can be generated correctly. (<strong><a href="https://redirect.github.com/axios/axios/issues/10898">#10898</a></strong>)</li>
</ul>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/axios/axios/commit/2d06f96e8602c2db13b65a26340ee4a1bbc0b61f"><code>2d06f96</code></a> chore(release): prepare release 1.18.0 (<a href="https://redirect.github.com/axios/axios/issues/11003">#11003</a>)</li>
<li><a href="https://github.com/axios/axios/commit/32fc489632377d214db55bfa4e2c48486a7d7ce2"><code>32fc489</code></a> fix: malformed http urls (<a href="https://redirect.github.com/axios/axios/issues/11000">#11000</a>)</li>
<li><a href="https://github.com/axios/axios/commit/b40ce498abfa10d90b873b4fd08f520afa5d2545"><code>b40ce49</code></a> chore(deps-dev): bump the development_dependencies group with 10 updates (<a href="https://redirect.github.com/axios/axios/issues/10">#10</a>...</li>
<li><a href="https://github.com/axios/axios/commit/fe964f960ecb52c3e1155b0daf7be77541956b01"><code>fe964f9</code></a> docs: mark proxy config as Node.js only (<a href="https://redirect.github.com/axios/axios/issues/10995">#10995</a>)</li>
<li><a href="https://github.com/axios/axios/commit/5f229d2d1f018d1db3dab6bbe034dbf3f9041b99"><code>5f229d2</code></a> chore(deps): bump actions/checkout from 6.0.2 to 6.0.3 in the github-actions ...</li>
<li><a href="https://github.com/axios/axios/commit/fae9d4e7db6a858c407c75e607a071c533c5c4f6"><code>fae9d4e</code></a> docs: clarify package update PR policy (<a href="https://redirect.github.com/axios/axios/issues/10992">#10992</a>)</li>
<li><a href="https://github.com/axios/axios/commit/28ab2ced820e55192806c53472ab3eb0cbb68dc2"><code>28ab2ce</code></a> chore(deps-dev): bump the development_dependencies group with 2 updates (<a href="https://redirect.github.com/axios/axios/issues/10989">#10989</a>)</li>
<li><a href="https://github.com/axios/axios/commit/a8e4f13aeecc45a3b8fab3ecfd9ddb5d70fb772b"><code>a8e4f13</code></a> fix(core): keep default validateStatus when request passes undefined (<a href="https://redirect.github.com/axios/axios/issues/10899">#10899</a>)</li>
<li><a href="https://github.com/axios/axios/commit/614f4552a17de757d4171ad7c3bd38c9c1025fd8"><code>614f455</code></a> docs: publish v1.17.0 release notes (<a href="https://redirect.github.com/axios/axios/issues/10988">#10988</a>)</li>
<li><a href="https://github.com/axios/axios/commit/6bb12c191f5380fad321322fb90216ae0dc36985"><code>6bb12c1</code></a> fix: custom auth headers not stripped on cross-origin redirects (<a href="https://redirect.github.com/axios/axios/issues/10892">#10892</a>)</li>
<li>Additional commits viewable in <a href="https://github.com/axios/axios/compare/v1.15.2...v1.18.0">compare view</a></li>
</ul>
</details>
<br />

---

## archived-130 — Bump immutable from 4.3.8 to 4.3.9 in /src/AutoNate.Spa

`MERGED (merged 2026-08-31)` · app/dependabot · opened 2026-08-31 · `dependabot/npm_and_yarn/src/AutoNate.Spa/immutable-4.3.9` → `master`

Bumps [immutable](https://github.com/immutable-js/immutable-js) from 4.3.8 to 4.3.9.
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/immutable-js/immutable-js/releases">immutable's releases</a>.</em></p>
<blockquote>
<h2>v4.3.9</h2>
<h1>What's changed</h1>
<ul>
<li>fix(List): guard oversized bounds in setListBounds. Fixes CVE <a href="https://github.com/immutable-js/immutable-js/security/advisories/GHSA-v56q-mh7h-f735">https://github.com/immutable-js/immutable-js/security/advisories/GHSA-v56q-mh7h-f735</a></li>
<li>perf(Map): index large hash-collision buckets for faster lookups. Fixes CVE <a href="https://github.com/immutable-js/immutable-js/security/advisories/GHSA-xvcm-6775-5m9r">https://github.com/immutable-js/immutable-js/security/advisories/GHSA-xvcm-6775-5m9r</a></li>
</ul>
<p><strong>Full Changelog</strong>: <a href="https://github.com/immutable-js/immutable-js/compare/v4.3.8...v4.3.9">https://github.com/immutable-js/immutable-js/compare/v4.3.8...v4.3.9</a></p>
</blockquote>
</details>
<details>
<summary>Changelog</summary>
<p><em>Sourced from <a href="https://github.com/immutable-js/immutable-js/blob/main/CHANGELOG.md">immutable's changelog</a>.</em></p>
<blockquote>
<h2>4.3.9</h2>
<ul>
<li>fix(List): guard oversized bounds in setListBounds. Fixes CVE <a href="https://github.com/immutable-js/immutable-js/security/advisories/GHSA-v56q-mh7h-f735">https://github.com/immutable-js/immutable-js/security/advisories/GHSA-v56q-mh7h-f735</a></li>
<li>perf(Map): index large hash-collision buckets for faster lookups. Fixes CVE <a href="https://github.com/immutable-js/immutable-js/security/advisories/GHSA-xvcm-6775-5m9r">https://github.com/immutable-js/immutable-js/security/advisories/GHSA-xvcm-6775-5m9r</a></li>
</ul>
<h2>5.1.7</h2>
<ul>
<li>fix(Repeat): lastIndexOf returned size instead of size - 1 by <a href="https://github.com/chatman-media"><code>@​chatman-media</code></a> in <a href="https://redirect.github.com/immutable-js/immutable-js/pull/2227">immutable-js/immutable-js#2227</a>. Fixes CVE <a href="https://github.com/immutable-js/immutable-js/security/advisories/GHSA-wf6x-7x77-mvgw">CVE-2026-29063 </a></li>
<li>fix(IndexedCollection): <code>has(index)</code> on a lazy <code>Seq</code> of unknown size now checks index existence instead of searching for a value equal to the index <a href="https://redirect.github.com/immutable-js/immutable-js/pull/2203">#2203</a></li>
<li>[TypeScript]: <code>reduce</code>/<code>reduceRight</code> without an initial value now infer the result type from the collection's values when the reducer returns a value (e.g. <code>list.reduce((a, b) =&gt; a + b)</code> infers <code>number</code>), matching <code>Array#reduce</code>. Previously an explicit type argument was required. <a href="https://redirect.github.com/immutable-js/immutable-js/pull/2205">#2205</a></li>
</ul>
<h2>5.1.6</h2>
<ul>
<li>fix(reverseFactory): read <code>reversedSequence.size</code> in <code>__iterator</code> instead of this <a href="https://redirect.github.com/immutable-js/immutable-js/pull/2196">#2196</a></li>
</ul>
<h2>5.1.5</h2>
<ul>
<li>Fix Improperly Controlled Modification of Object Prototype Attributes ('Prototype Pollution') in immutable</li>
</ul>
<h2>5.1.4</h2>
<ul>
<li>Migrate some files to TS by <a href="https://github.com/jdeniau"><code>@​jdeniau</code></a> in <a href="https://redirect.github.com/immutable-js/immutable-js/pull/2125">immutable-js/immutable-js#2125</a>
<ul>
<li>Iterator.ts</li>
<li>PairSorting.ts</li>
<li>toJS.ts</li>
<li>Math.ts</li>
<li>Hash.ts</li>
</ul>
</li>
<li>Extract CollectionHelperMethods and convert to TS by <a href="https://github.com/jdeniau"><code>@​jdeniau</code></a> in <a href="https://redirect.github.com/immutable-js/immutable-js/pull/2131">immutable-js/immutable-js#2131</a></li>
<li>Use npm <a href="https://docs.npmjs.com/trusted-publishers">trusted publishing only</a> to avoid token stealing.</li>
</ul>
<h3>Documentation</h3>
<ul>
<li>Fix/a11y issues by <a href="https://github.com/lyannel"><code>@​lyannel</code></a> in <a href="https://redirect.github.com/immutable-js/immutable-js/pull/2136">immutable-js/immutable-js#2136</a></li>
<li>Doc add Map.get signature update by <a href="https://github.com/borracciaBlu"><code>@​borracciaBlu</code></a> in <a href="https://redirect.github.com/immutable-js/immutable-js/pull/2138">immutable-js/immutable-js#2138</a></li>
<li>fix(doc):minor-issues#2132 by <a href="https://github.com/JayMeDotDot"><code>@​JayMeDotDot</code></a> in <a href="https://redirect.github.com/immutable-js/immutable-js/pull/2133">immutable-js/immutable-js#2133</a></li>
<li>Fix algolia search by <a href="https://github.com/jdeniau"><code>@​jdeniau</code></a> in <a href="https://redirect.github.com/immutable-js/immutable-js/pull/2135">immutable-js/immutable-js#2135</a></li>
<li>Typo in OrderedMap by <a href="https://github.com/jdeniau"><code>@​jdeniau</code></a> in <a href="https://redirect.github.com/immutable-js/immutable-js/pull/2144">immutable-js/immutable-js#2144</a></li>
</ul>
<h3>Internal</h3>
<ul>
<li>chore: Sort all imports and activate eslint import rule by <a href="https://github.com/jdeniau"><code>@​jdeniau</code></a> in <a href="https://redirect.github.com/immutable-js/immutable-js/pull/2119">immutable-js/immutable-js#2119</a></li>
</ul>
<h2>5.1.3</h2>
<h3>TypeScript</h3>
<ul>
<li>fix: allow readonly map entry constructor by <a href="https://github.com/septs"><code>@​septs</code></a> in <a href="https://redirect.github.com/immutable-js/immutable-js/pull/2123">immutable-js/immutable-js#2123</a></li>
</ul>
<h3>Documentation</h3>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/immutable-js/immutable-js/commit/5da7919616a015cc03173f8f0bd3ee7101ab27f2"><code>5da7919</code></a> 4.3.9</li>
<li><a href="https://github.com/immutable-js/immutable-js/commit/794a1a9148e2af3b4d282047917e1e0c11b774bc"><code>794a1a9</code></a> Merge commit from fork</li>
<li><a href="https://github.com/immutable-js/immutable-js/commit/3dd7e5655012597a41873e328bf9142a8901527b"><code>3dd7e56</code></a> perf(Map): index large hash-collision buckets for faster lookups</li>
<li><a href="https://github.com/immutable-js/immutable-js/commit/62d0b58553813d86987009bd106aa56e0c4459c5"><code>62d0b58</code></a> fix ts in tests</li>
<li><a href="https://github.com/immutable-js/immutable-js/commit/8c0e5f806494cebb4fd75bcbf5846963c866ef5b"><code>8c0e5f8</code></a> Merge commit from fork</li>
<li><a href="https://github.com/immutable-js/immutable-js/commit/f0bc997d8eb9886aff2236635aa210a95a04304a"><code>f0bc997</code></a> Merge commit from fork</li>
<li><a href="https://github.com/immutable-js/immutable-js/commit/8ac83f4eba62469e284881728aa636a1e786f87e"><code>8ac83f4</code></a> change tag</li>
<li><a href="https://github.com/immutable-js/immutable-js/commit/f7373e5e9e04aa7049b5991d5623eec272e3c06a"><code>f7373e5</code></a> use id-token to deploy 4.x version</li>
<li><a href="https://github.com/immutable-js/immutable-js/commit/2f545ad3f3eecae40c1f732c8cb8b7faab22493a"><code>2f545ad</code></a> changelog</li>
<li>See full diff in <a href="https://github.com/immutable-js/immutable-js/compare/v4.3.8...v4.3.9">compare view</a></li>
</ul>
</details>
<br />


[![Dependabot compatibility score](https://dependabot-badges.githubapp.com/badges/compatibility_score?dependency-name=immutable&package-manager=npm_and_yarn&previous-version=4.3.8&new-version=4.3.9)](https://docs.github.com/en/github/managing-security-vulnerabilities/about-dependabot-security-updates#about-compatibility-scores)

Dependabot will resolve any conflicts with this PR as long as you don't alter it yourself. You can also trigger a rebase manually by commenting `@dependabot rebase`.

[//]: # (dependabot-automerge-start)
[//]: # (dependabot-automerge-end)

---

<details>
<summary>Dependabot commands and options</summary>
<br />

You can trigger Dependabot actions by commenting on this PR:
- `@dependabot rebase` will rebase this PR
- `@dependabot recreate` will recreate this PR, overwriting any edits that have been made to it
- `@dependabot show <dependency name> ignore conditions` will show all of the ignore conditions of the specified dependency
- `@dependabot ignore this major version` will close this PR and stop Dependabot creating any more for this major version (unless you reopen the PR or upgrade to it yourself)
- `@dependabot ignore this minor version` will close this PR and stop Dependabot creating any more for this minor version (unless you reopen the PR or upgrade to it yourself)
- `@dependabot ignore this dependency` will close this PR and stop Dependabot creating any more for this dependency (unless you reopen the PR or upgrade to it yourself)
You can disable automated security fix PRs for this repo from the [Security Alerts page](https://github.com/nathanpond/AutoNate/network/alerts).

</details>

---

## archived-131 — Bump the spa-minor-patch group across 1 directory with 38 updates

`CLOSED` · app/dependabot · opened 2026-08-31 · `dependabot/npm_and_yarn/src/AutoNate.Spa/spa-minor-patch-1b4e4f12d7` → `master`

Bumps the spa-minor-patch group with 38 updates in the /src/AutoNate.Spa directory:

| Package | From | To |
| --- | --- | --- |
| [@blocknote/core](https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/core) | `0.51.0` | `0.54.0` |
| [@blocknote/mantine](https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/mantine) | `0.51.0` | `0.54.0` |
| [@blocknote/react](https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/react) | `0.51.0` | `0.54.0` |
| [@codemirror/lang-html](https://github.com/codemirror/lang-html) | `6.4.11` | `6.4.12` |
| [@eigenpal/docx-editor-agents](https://github.com/eigenpal/docx-editor/tree/HEAD/packages/agents) | `1.0.3` | `1.9.0` |
| [@eigenpal/docx-editor-core](https://github.com/eigenpal/docx-editor/tree/HEAD/packages/core) | `1.0.3` | `1.9.0` |
| [@eigenpal/docx-editor-i18n](https://github.com/eigenpal/docx-editor/tree/HEAD/packages/i18n) | `1.0.3` | `1.9.0` |
| [@eigenpal/docx-editor-react](https://github.com/eigenpal/docx-editor/tree/HEAD/packages/react) | `1.0.3` | `1.9.0` |
| [@fortawesome/fontawesome-free](https://github.com/FortAwesome/Font-Awesome) | `7.2.0` | `7.3.1` |
| [@hocuspocus/provider](https://github.com/ueberdosis/hocuspocus) | `4.0.0` | `4.6.0` |
| [@mantine/charts](https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts) | `9.1.1` | `9.5.2` |
| [@mantine/colors-generator](https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator) | `9.1.1` | `9.5.2` |
| [@mantine/core](https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/core) | `9.1.1` | `9.5.2` |
| [@mantine/dates](https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dates) | `9.1.1` | `9.5.2` |
| [@mantine/dropzone](https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dropzone) | `9.1.1` | `9.5.2` |
| [@mantine/form](https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/form) | `9.1.1` | `9.5.2` |
| [@mantine/hooks](https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/hooks) | `9.1.1` | `9.5.2` |
| [@mantine/modals](https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/modals) | `9.1.1` | `9.5.2` |
| [@mantine/notifications](https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/notifications) | `9.1.1` | `9.5.2` |
| [@tanstack/react-query](https://github.com/TanStack/query/tree/HEAD/packages/react-query) | `5.100.1` | `5.102.8` |
| [@tanstack/react-query-devtools](https://github.com/TanStack/query/tree/HEAD/packages/react-query-devtools) | `5.100.1` | `5.102.8` |
| [@uiw/react-codemirror](https://github.com/uiwjs/react-codemirror) | `4.25.9` | `4.25.11` |
| [@xyflow/react](https://github.com/xyflow/xyflow/tree/HEAD/packages/react) | `12.10.2` | `12.11.5` |
| [axios](https://github.com/axios/axios) | `1.18.0` | `1.20.0` |
| [marked](https://github.com/markedjs/marked) | `18.0.4` | `18.0.11` |
| [react](https://github.com/react/react/tree/HEAD/packages/react) | `19.2.5` | `19.2.8` |
| [@types/react](https://github.com/DefinitelyTyped/DefinitelyTyped/tree/HEAD/types/react) | `19.2.14` | `19.2.18` |
| [react-dom](https://github.com/react/react/tree/HEAD/packages/react-dom) | `19.2.5` | `19.2.8` |
| [@types/react-dom](https://github.com/DefinitelyTyped/DefinitelyTyped/tree/HEAD/types/react-dom) | `19.2.3` | `19.2.5` |
| [react-grid-layout](https://github.com/STRML/react-grid-layout) | `2.2.3` | `2.2.4` |
| [@types/react-grid-layout](https://github.com/DefinitelyTyped/DefinitelyTyped/tree/HEAD/types/react-grid-layout) | `1.3.6` | `2.1.0` |
| [recharts](https://github.com/recharts/recharts) | `3.8.1` | `3.10.1` |
| [yjs](https://github.com/yjs/yjs) | `13.6.30` | `13.6.32` |
| [zod](https://github.com/colinhacks/zod) | `4.3.6` | `4.4.3` |
| [@vitejs/plugin-react](https://github.com/vitejs/vite-plugin-react/tree/HEAD/packages/plugin-react) | `6.0.1` | `6.1.1` |
| [globals](https://github.com/sindresorhus/globals) | `17.6.0` | `17.11.0` |
| [typescript-eslint](https://github.com/typescript-eslint/typescript-eslint/tree/HEAD/packages/typescript-eslint) | `8.60.0` | `8.68.0` |
| [vite](https://github.com/vitejs/vite/tree/HEAD/packages/vite) | `8.0.10` | `8.2.2` |


Updates `@blocknote/core` from 0.51.0 to 0.54.0
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/TypeCellOS/BlockNote/releases">@​blocknote/core's releases</a>.</em></p>
<blockquote>
<h2>v0.54.0</h2>
<h2>0.54.0 (2026-08-13)</h2>
<p>💖 The math block and diagram block has been sponsored by <a href="https://www.numerique.gouv.fr/dinum/">DINUM</a> 🇫🇷</p>
<h3>Math Block</h3>
<p>A long requested feature, you can now add block &amp; inline math to a BlockNote editor. They are driven by <a href="https://katex.org/">Katex</a> &amp; support much of <a href="https://www.latex-project.org/">Latex</a> for all your notation needs.</p>
<p><a href="https://github.com/user-attachments/assets/8fb5790e-6922-4f02-a35f-27c791b877e8">https://github.com/user-attachments/assets/8fb5790e-6922-4f02-a35f-27c791b877e8</a></p>
<p><a href="https://www.blocknotejs.org/examples/custom-schema/math-block">Link to demo</a></p>
<h3>Diagram Block</h3>
<p>We've also added support for a diagram block driven by <a href="https://mermaid.js.org/">Mermaid.js</a>, allowing you to add diagramming to the editor.</p>
<p><a href="https://github.com/user-attachments/assets/0a64e98a-5bf0-4dec-b1a4-84ccf98f4a70">https://github.com/user-attachments/assets/0a64e98a-5bf0-4dec-b1a4-84ccf98f4a70</a></p>
<p><a href="https://www.blocknotejs.org/examples/custom-schema/diagram-block">Link to demo</a></p>
<h3>Source Block with Preview</h3>
<p>Both the Math block &amp; Diagram block are built on a primitive that you can build your own custom blocks from. The Source Block with Preview primitive allows you to build a pair of a block which renders content with an inline editor for the content being rendered. This can enable other sorts of preview-like features in the future, exposed as an API for you to build your own custom blocks with.</p>
<!-- raw HTML omitted -->
<!-- raw HTML omitted -->
<p><a href="https://www.blocknotejs.org/examples/custom-schema/source-with-preview">Link to demo</a></p>
<h3>🚀 Features</h3>
<ul>
<li>Adds a Math block (<a href="https://github.com/TypeCellOS/BlockNote/commit/2a34f7d70">2a34f7d70</a>)</li>
<li>Adds a Diagram block (<a href="https://github.com/TypeCellOS/BlockNote/commit/0fca0ee7a">0fca0ee7a</a>)</li>
<li><strong>core:</strong> Source-with-preview, syntax highlighting &amp; exporter images (<a href="https://github.com/TypeCellOS/BlockNote/commit/503c796d3">503c796d3</a>)</li>
</ul>
<h3>🩹 Fixes</h3>
<ul>
<li><strong>ai:</strong> Operations on collaborative documents (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2952">#2952</a>)</li>
<li><strong>ai:</strong> Operations on blocks containing comments (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2953">#2953</a>)</li>
<li><strong>pdf:</strong> Add custom font and fontFamily options for CJK (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2945">#2945</a>)</li>
<li>Expose first suggestion as active descendant (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2965">#2965</a>)</li>
<li><strong>xl-docx-exporter:</strong> Clamp list nesting to the levels DOCX defines (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2969">#2969</a>)</li>
</ul>
<h3>❤️ Thank You</h3>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Changelog</summary>
<p><em>Sourced from <a href="https://github.com/TypeCellOS/BlockNote/blob/main/CHANGELOG.md">@​blocknote/core's changelog</a>.</em></p>
<blockquote>
<h2>0.54.0 (2026-08-13)</h2>
<h3>🚀 Features</h3>
<ul>
<li>Adds a Math block (<a href="https://github.com/TypeCellOS/BlockNote/commit/2a34f7d70">2a34f7d70</a>)</li>
<li>Adds a Diagram block (<a href="https://github.com/TypeCellOS/BlockNote/commit/0fca0ee7a">0fca0ee7a</a>)</li>
<li><strong>core:</strong> Source-with-preview, syntax highlighting &amp; exporter images (<a href="https://github.com/TypeCellOS/BlockNote/commit/503c796d3">503c796d3</a>)</li>
</ul>
<h3>🩹 Fixes</h3>
<ul>
<li><strong>ai:</strong> Operations on collaborative documents (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2952">#2952</a>)</li>
<li><strong>ai:</strong> Operations on blocks containing comments (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2953">#2953</a>)</li>
<li><strong>pdf:</strong> Add custom font and fontFamily options for CJK (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2945">#2945</a>)</li>
<li>Expose first suggestion as active descendant (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2965">#2965</a>)</li>
<li><strong>xl-docx-exporter:</strong> Clamp list nesting to the levels DOCX defines (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2969">#2969</a>)</li>
</ul>
<h3>❤️ Thank You</h3>
<ul>
<li>Adarshsm <a href="mailto:adarshmudugal@gmail.com">adarshmudugal@gmail.com</a></li>
<li>Nick The Sick (<a href="https://github.com/nperez0111"><code>@​nperez0111</code></a>)</li>
<li>Pupuking723 <a href="mailto:2318857637@qq.com">2318857637@qq.com</a></li>
</ul>
<h2>0.53.0 (2026-08-06)</h2>
<h3>🚀 Features</h3>
<ul>
<li><strong>shadcn:</strong> ⚠️ Use base-ui instead of radix (BLO-1279) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2913">#2913</a>)</li>
</ul>
<h3>🩹 Fixes</h3>
<ul>
<li>getCellSelection throwing error in positions (BLO-1193) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2911">#2911</a>)</li>
<li>Multi-column slash menu items within a column (BLO-905) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2914">#2914</a>)</li>
<li>Suggestion menu behaviour (BLO-1283, BLO-955) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2930">#2930</a>)</li>
<li>Ignore useless block/inline content mutations (BLO-1224) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2912">#2912</a>)</li>
<li><strong>slash-menu:</strong> Better overflow behavior (BLO-1192) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2909">#2909</a>)</li>
<li>Slash menu item selection behaviour (BLO-1222) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2838">#2838</a>)</li>
<li>HTML export/parse round trip ignoring empty blocks (BLO-873) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2931">#2931</a>)</li>
<li><strong>core:</strong> Guard getBlock() calls to prevent TypeError on stale blocks (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2941">#2941</a>)</li>
<li>Stop stale node view positions crashing the editor (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2938">#2938</a>)</li>
<li>Multi-column trailing blocks, column hover borders &amp; drop cursor left edge BLO-1226 (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2885">#2885</a>)</li>
</ul>
<h4>⚠️ Breaking Changes</h4>
<ul>
<li><strong>shadcn:</strong> ⚠️ Use base-ui instead of radix (BLO-1279) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2913">#2913</a>)</li>
</ul>
<h3>❤️ Thank You</h3>
<ul>
<li>Yousef</li>
<li>Nick Perez <a href="mailto:nick@blocknotejs.org">nick@blocknotejs.org</a></li>
<li>Matthew Lipski (<a href="https://github.com/matthewlipski"><code>@​matthewlipski</code></a>)</li>
</ul>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/ea5d80358f179d1683abcd2e0e3e9d547bf52eef"><code>ea5d803</code></a> chore(release): v0.54.0</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/503c796d37f2c8734cf65e9bad3348127043c63b"><code>503c796</code></a> feat(core): source-with-preview, syntax highlighting &amp; exporter images</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/99253c3814a93e6f5d1ae318efeb0b10df90f32d"><code>99253c3</code></a> chore: migrate to TypeScript 7 and consolidate the <a href="https://github.com/shared"><code>@​shared</code></a> alias</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/bea469e31eab19242b1238cd3600a14c1d6148c1"><code>bea469e</code></a> refactor: vendor <code>@​tanstack/store</code> as a first-party Store (<a href="https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/core/issues/2956">#2956</a>)</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/dee3401a2647eb01b7a982b32e98e0bd182713fe"><code>dee3401</code></a> chore: bump prosemirror-view to ^1.42.2 (<a href="https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/core/issues/2954">#2954</a>)</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/decb3d21480ceed983d3befb4e87ff8d26bcc938"><code>decb3d2</code></a> fix(ai): operations on blocks containing comments (<a href="https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/core/issues/2953">#2953</a>)</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/824abce757ed1a44e4dbb048fe88ea954b592831"><code>824abce</code></a> fix(ai): operations on collaborative documents (<a href="https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/core/issues/2952">#2952</a>)</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/529c3b02f6e413c362e96718dd712dd4b4c495a0"><code>529c3b0</code></a> chore(release): v0.53.0</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/d998f0168abd54ec57239479ea2dfc3d17df6a1a"><code>d998f01</code></a> fix: multi-column trailing blocks, column hover borders &amp; drop cursor left ed...</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/58d43ff08806ce078f03cf5a28afeefb1bede482"><code>58d43ff</code></a> fix: stop stale node view positions crashing the editor (<a href="https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/core/issues/2938">#2938</a>)</li>
<li>Additional commits viewable in <a href="https://github.com/TypeCellOS/BlockNote/commits/v0.54.0/packages/core">compare view</a></li>
</ul>
</details>
<br />

Updates `@blocknote/mantine` from 0.51.0 to 0.54.0
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/TypeCellOS/BlockNote/releases">@​blocknote/mantine's releases</a>.</em></p>
<blockquote>
<h2>v0.54.0</h2>
<h2>0.54.0 (2026-08-13)</h2>
<p>💖 The math block and diagram block has been sponsored by <a href="https://www.numerique.gouv.fr/dinum/">DINUM</a> 🇫🇷</p>
<h3>Math Block</h3>
<p>A long requested feature, you can now add block &amp; inline math to a BlockNote editor. They are driven by <a href="https://katex.org/">Katex</a> &amp; support much of <a href="https://www.latex-project.org/">Latex</a> for all your notation needs.</p>
<p><a href="https://github.com/user-attachments/assets/8fb5790e-6922-4f02-a35f-27c791b877e8">https://github.com/user-attachments/assets/8fb5790e-6922-4f02-a35f-27c791b877e8</a></p>
<p><a href="https://www.blocknotejs.org/examples/custom-schema/math-block">Link to demo</a></p>
<h3>Diagram Block</h3>
<p>We've also added support for a diagram block driven by <a href="https://mermaid.js.org/">Mermaid.js</a>, allowing you to add diagramming to the editor.</p>
<p><a href="https://github.com/user-attachments/assets/0a64e98a-5bf0-4dec-b1a4-84ccf98f4a70">https://github.com/user-attachments/assets/0a64e98a-5bf0-4dec-b1a4-84ccf98f4a70</a></p>
<p><a href="https://www.blocknotejs.org/examples/custom-schema/diagram-block">Link to demo</a></p>
<h3>Source Block with Preview</h3>
<p>Both the Math block &amp; Diagram block are built on a primitive that you can build your own custom blocks from. The Source Block with Preview primitive allows you to build a pair of a block which renders content with an inline editor for the content being rendered. This can enable other sorts of preview-like features in the future, exposed as an API for you to build your own custom blocks with.</p>
<!-- raw HTML omitted -->
<!-- raw HTML omitted -->
<p><a href="https://www.blocknotejs.org/examples/custom-schema/source-with-preview">Link to demo</a></p>
<h3>🚀 Features</h3>
<ul>
<li>Adds a Math block (<a href="https://github.com/TypeCellOS/BlockNote/commit/2a34f7d70">2a34f7d70</a>)</li>
<li>Adds a Diagram block (<a href="https://github.com/TypeCellOS/BlockNote/commit/0fca0ee7a">0fca0ee7a</a>)</li>
<li><strong>core:</strong> Source-with-preview, syntax highlighting &amp; exporter images (<a href="https://github.com/TypeCellOS/BlockNote/commit/503c796d3">503c796d3</a>)</li>
</ul>
<h3>🩹 Fixes</h3>
<ul>
<li><strong>ai:</strong> Operations on collaborative documents (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2952">#2952</a>)</li>
<li><strong>ai:</strong> Operations on blocks containing comments (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2953">#2953</a>)</li>
<li><strong>pdf:</strong> Add custom font and fontFamily options for CJK (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2945">#2945</a>)</li>
<li>Expose first suggestion as active descendant (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2965">#2965</a>)</li>
<li><strong>xl-docx-exporter:</strong> Clamp list nesting to the levels DOCX defines (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2969">#2969</a>)</li>
</ul>
<h3>❤️ Thank You</h3>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Changelog</summary>
<p><em>Sourced from <a href="https://github.com/TypeCellOS/BlockNote/blob/main/CHANGELOG.md">@​blocknote/mantine's changelog</a>.</em></p>
<blockquote>
<h2>0.54.0 (2026-08-13)</h2>
<h3>🚀 Features</h3>
<ul>
<li>Adds a Math block (<a href="https://github.com/TypeCellOS/BlockNote/commit/2a34f7d70">2a34f7d70</a>)</li>
<li>Adds a Diagram block (<a href="https://github.com/TypeCellOS/BlockNote/commit/0fca0ee7a">0fca0ee7a</a>)</li>
<li><strong>core:</strong> Source-with-preview, syntax highlighting &amp; exporter images (<a href="https://github.com/TypeCellOS/BlockNote/commit/503c796d3">503c796d3</a>)</li>
</ul>
<h3>🩹 Fixes</h3>
<ul>
<li><strong>ai:</strong> Operations on collaborative documents (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2952">#2952</a>)</li>
<li><strong>ai:</strong> Operations on blocks containing comments (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2953">#2953</a>)</li>
<li><strong>pdf:</strong> Add custom font and fontFamily options for CJK (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2945">#2945</a>)</li>
<li>Expose first suggestion as active descendant (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2965">#2965</a>)</li>
<li><strong>xl-docx-exporter:</strong> Clamp list nesting to the levels DOCX defines (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2969">#2969</a>)</li>
</ul>
<h3>❤️ Thank You</h3>
<ul>
<li>Adarshsm <a href="mailto:adarshmudugal@gmail.com">adarshmudugal@gmail.com</a></li>
<li>Nick The Sick (<a href="https://github.com/nperez0111"><code>@​nperez0111</code></a>)</li>
<li>Pupuking723 <a href="mailto:2318857637@qq.com">2318857637@qq.com</a></li>
</ul>
<h2>0.53.0 (2026-08-06)</h2>
<h3>🚀 Features</h3>
<ul>
<li><strong>shadcn:</strong> ⚠️ Use base-ui instead of radix (BLO-1279) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2913">#2913</a>)</li>
</ul>
<h3>🩹 Fixes</h3>
<ul>
<li>getCellSelection throwing error in positions (BLO-1193) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2911">#2911</a>)</li>
<li>Multi-column slash menu items within a column (BLO-905) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2914">#2914</a>)</li>
<li>Suggestion menu behaviour (BLO-1283, BLO-955) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2930">#2930</a>)</li>
<li>Ignore useless block/inline content mutations (BLO-1224) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2912">#2912</a>)</li>
<li><strong>slash-menu:</strong> Better overflow behavior (BLO-1192) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2909">#2909</a>)</li>
<li>Slash menu item selection behaviour (BLO-1222) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2838">#2838</a>)</li>
<li>HTML export/parse round trip ignoring empty blocks (BLO-873) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2931">#2931</a>)</li>
<li><strong>core:</strong> Guard getBlock() calls to prevent TypeError on stale blocks (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2941">#2941</a>)</li>
<li>Stop stale node view positions crashing the editor (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2938">#2938</a>)</li>
<li>Multi-column trailing blocks, column hover borders &amp; drop cursor left edge BLO-1226 (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2885">#2885</a>)</li>
</ul>
<h4>⚠️ Breaking Changes</h4>
<ul>
<li><strong>shadcn:</strong> ⚠️ Use base-ui instead of radix (BLO-1279) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2913">#2913</a>)</li>
</ul>
<h3>❤️ Thank You</h3>
<ul>
<li>Yousef</li>
<li>Nick Perez <a href="mailto:nick@blocknotejs.org">nick@blocknotejs.org</a></li>
<li>Matthew Lipski (<a href="https://github.com/matthewlipski"><code>@​matthewlipski</code></a>)</li>
</ul>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/ea5d80358f179d1683abcd2e0e3e9d547bf52eef"><code>ea5d803</code></a> chore(release): v0.54.0</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/99253c3814a93e6f5d1ae318efeb0b10df90f32d"><code>99253c3</code></a> chore: migrate to TypeScript 7 and consolidate the <a href="https://github.com/shared"><code>@​shared</code></a> alias</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/529c3b02f6e413c362e96718dd712dd4b4c495a0"><code>529c3b0</code></a> chore(release): v0.53.0</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/47d864c6e997963281af4df5ec54a4421773c134"><code>47d864c</code></a> fix(slash-menu): better overflow behavior (BLO-1192) (<a href="https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/mantine/issues/2909">#2909</a>)</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/8288b926e8a34737f287da1310e709b4785e2461"><code>8288b92</code></a> style: grid suggestion menu item padding (BLO-1225) (<a href="https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/mantine/issues/2910">#2910</a>)</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/dee7880b89b1e9bc00b4f4481f32652c7a4b4408"><code>dee7880</code></a> chore(release): v0.52.1</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/a99aab441b5db07c35d9f5ce406ea1676c6314ca"><code>a99aab4</code></a> chore(release): v0.52.0</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/030dcf0d133d99a173b8fa44ceec11b07a82867e"><code>030dcf0</code></a> refactor(versioning): consolidate sidebar CSS into the shared stylesheet</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/ef34ecca53f6d4c6a3cb0fa4d1058424e9a9124f"><code>ef34ecc</code></a> refactor(ui): forward refs in AttributionTooltip implementations</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/161a6147c09b81a0fc5af97afcc8606111481e4a"><code>161a614</code></a> fix(versioning): make yhub history snapshot ids unique and fix grouping</li>
<li>Additional commits viewable in <a href="https://github.com/TypeCellOS/BlockNote/commits/v0.54.0/packages/mantine">compare view</a></li>
</ul>
</details>
<br />

Updates `@blocknote/react` from 0.51.0 to 0.54.0
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/TypeCellOS/BlockNote/releases">@​blocknote/react's releases</a>.</em></p>
<blockquote>
<h2>v0.54.0</h2>
<h2>0.54.0 (2026-08-13)</h2>
<p>💖 The math block and diagram block has been sponsored by <a href="https://www.numerique.gouv.fr/dinum/">DINUM</a> 🇫🇷</p>
<h3>Math Block</h3>
<p>A long requested feature, you can now add block &amp; inline math to a BlockNote editor. They are driven by <a href="https://katex.org/">Katex</a> &amp; support much of <a href="https://www.latex-project.org/">Latex</a> for all your notation needs.</p>
<p><a href="https://github.com/user-attachments/assets/8fb5790e-6922-4f02-a35f-27c791b877e8">https://github.com/user-attachments/assets/8fb5790e-6922-4f02-a35f-27c791b877e8</a></p>
<p><a href="https://www.blocknotejs.org/examples/custom-schema/math-block">Link to demo</a></p>
<h3>Diagram Block</h3>
<p>We've also added support for a diagram block driven by <a href="https://mermaid.js.org/">Mermaid.js</a>, allowing you to add diagramming to the editor.</p>
<p><a href="https://github.com/user-attachments/assets/0a64e98a-5bf0-4dec-b1a4-84ccf98f4a70">https://github.com/user-attachments/assets/0a64e98a-5bf0-4dec-b1a4-84ccf98f4a70</a></p>
<p><a href="https://www.blocknotejs.org/examples/custom-schema/diagram-block">Link to demo</a></p>
<h3>Source Block with Preview</h3>
<p>Both the Math block &amp; Diagram block are built on a primitive that you can build your own custom blocks from. The Source Block with Preview primitive allows you to build a pair of a block which renders content with an inline editor for the content being rendered. This can enable other sorts of preview-like features in the future, exposed as an API for you to build your own custom blocks with.</p>
<!-- raw HTML omitted -->
<!-- raw HTML omitted -->
<p><a href="https://www.blocknotejs.org/examples/custom-schema/source-with-preview">Link to demo</a></p>
<h3>🚀 Features</h3>
<ul>
<li>Adds a Math block (<a href="https://github.com/TypeCellOS/BlockNote/commit/2a34f7d70">2a34f7d70</a>)</li>
<li>Adds a Diagram block (<a href="https://github.com/TypeCellOS/BlockNote/commit/0fca0ee7a">0fca0ee7a</a>)</li>
<li><strong>core:</strong> Source-with-preview, syntax highlighting &amp; exporter images (<a href="https://github.com/TypeCellOS/BlockNote/commit/503c796d3">503c796d3</a>)</li>
</ul>
<h3>🩹 Fixes</h3>
<ul>
<li><strong>ai:</strong> Operations on collaborative documents (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2952">#2952</a>)</li>
<li><strong>ai:</strong> Operations on blocks containing comments (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2953">#2953</a>)</li>
<li><strong>pdf:</strong> Add custom font and fontFamily options for CJK (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2945">#2945</a>)</li>
<li>Expose first suggestion as active descendant (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2965">#2965</a>)</li>
<li><strong>xl-docx-exporter:</strong> Clamp list nesting to the levels DOCX defines (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2969">#2969</a>)</li>
</ul>
<h3>❤️ Thank You</h3>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Changelog</summary>
<p><em>Sourced from <a href="https://github.com/TypeCellOS/BlockNote/blob/main/CHANGELOG.md">@​blocknote/react's changelog</a>.</em></p>
<blockquote>
<h2>0.54.0 (2026-08-13)</h2>
<h3>🚀 Features</h3>
<ul>
<li>Adds a Math block (<a href="https://github.com/TypeCellOS/BlockNote/commit/2a34f7d70">2a34f7d70</a>)</li>
<li>Adds a Diagram block (<a href="https://github.com/TypeCellOS/BlockNote/commit/0fca0ee7a">0fca0ee7a</a>)</li>
<li><strong>core:</strong> Source-with-preview, syntax highlighting &amp; exporter images (<a href="https://github.com/TypeCellOS/BlockNote/commit/503c796d3">503c796d3</a>)</li>
</ul>
<h3>🩹 Fixes</h3>
<ul>
<li><strong>ai:</strong> Operations on collaborative documents (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2952">#2952</a>)</li>
<li><strong>ai:</strong> Operations on blocks containing comments (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2953">#2953</a>)</li>
<li><strong>pdf:</strong> Add custom font and fontFamily options for CJK (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2945">#2945</a>)</li>
<li>Expose first suggestion as active descendant (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2965">#2965</a>)</li>
<li><strong>xl-docx-exporter:</strong> Clamp list nesting to the levels DOCX defines (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2969">#2969</a>)</li>
</ul>
<h3>❤️ Thank You</h3>
<ul>
<li>Adarshsm <a href="mailto:adarshmudugal@gmail.com">adarshmudugal@gmail.com</a></li>
<li>Nick The Sick (<a href="https://github.com/nperez0111"><code>@​nperez0111</code></a>)</li>
<li>Pupuking723 <a href="mailto:2318857637@qq.com">2318857637@qq.com</a></li>
</ul>
<h2>0.53.0 (2026-08-06)</h2>
<h3>🚀 Features</h3>
<ul>
<li><strong>shadcn:</strong> ⚠️ Use base-ui instead of radix (BLO-1279) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2913">#2913</a>)</li>
</ul>
<h3>🩹 Fixes</h3>
<ul>
<li>getCellSelection throwing error in positions (BLO-1193) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2911">#2911</a>)</li>
<li>Multi-column slash menu items within a column (BLO-905) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2914">#2914</a>)</li>
<li>Suggestion menu behaviour (BLO-1283, BLO-955) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2930">#2930</a>)</li>
<li>Ignore useless block/inline content mutations (BLO-1224) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2912">#2912</a>)</li>
<li><strong>slash-menu:</strong> Better overflow behavior (BLO-1192) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2909">#2909</a>)</li>
<li>Slash menu item selection behaviour (BLO-1222) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2838">#2838</a>)</li>
<li>HTML export/parse round trip ignoring empty blocks (BLO-873) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2931">#2931</a>)</li>
<li><strong>core:</strong> Guard getBlock() calls to prevent TypeError on stale blocks (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2941">#2941</a>)</li>
<li>Stop stale node view positions crashing the editor (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2938">#2938</a>)</li>
<li>Multi-column trailing blocks, column hover borders &amp; drop cursor left edge BLO-1226 (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2885">#2885</a>)</li>
</ul>
<h4>⚠️ Breaking Changes</h4>
<ul>
<li><strong>shadcn:</strong> ⚠️ Use base-ui instead of radix (BLO-1279) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2913">#2913</a>)</li>
</ul>
<h3>❤️ Thank You</h3>
<ul>
<li>Yousef</li>
<li>Nick Perez <a href="mailto:nick@blocknotejs.org">nick@blocknotejs.org</a></li>
<li>Matthew Lipski (<a href="https://github.com/matthewlipski"><code>@​matthewlipski</code></a>)</li>
</ul>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/ea5d80358f179d1683abcd2e0e3e9d547bf52eef"><code>ea5d803</code></a> chore(release): v0.54.0</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/503c796d37f2c8734cf65e9bad3348127043c63b"><code>503c796</code></a> feat(core): source-with-preview, syntax highlighting &amp; exporter images</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/99253c3814a93e6f5d1ae318efeb0b10df90f32d"><code>99253c3</code></a> chore: migrate to TypeScript 7 and consolidate the <a href="https://github.com/shared"><code>@​shared</code></a> alias</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/115d4333660a15391eea073ac7e7dd3ddb9da69a"><code>115d433</code></a> fix: expose first suggestion as active descendant (<a href="https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/react/issues/2965">#2965</a>)</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/bea469e31eab19242b1238cd3600a14c1d6148c1"><code>bea469e</code></a> refactor: vendor <code>@​tanstack/store</code> as a first-party Store (<a href="https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/react/issues/2956">#2956</a>)</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/529c3b02f6e413c362e96718dd712dd4b4c495a0"><code>529c3b0</code></a> chore(release): v0.53.0</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/d998f0168abd54ec57239479ea2dfc3d17df6a1a"><code>d998f01</code></a> fix: multi-column trailing blocks, column hover borders &amp; drop cursor left ed...</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/58d43ff08806ce078f03cf5a28afeefb1bede482"><code>58d43ff</code></a> fix: stop stale node view positions crashing the editor (<a href="https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/react/issues/2938">#2938</a>)</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/c32f9680082dc57c4bb2782a424ac67574a5713c"><code>c32f968</code></a> fix(core): guard getBlock() calls to prevent TypeError on stale blocks (<a href="https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/react/issues/2941">#2941</a>)</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/dee7880b89b1e9bc00b4f4481f32652c7a4b4408"><code>dee7880</code></a> chore(release): v0.52.1</li>
<li>Additional commits viewable in <a href="https://github.com/TypeCellOS/BlockNote/commits/v0.54.0/packages/react">compare view</a></li>
</ul>
</details>
<br />

Updates `@codemirror/lang-html` from 6.4.11 to 6.4.12
<details>
<summary>Commits</summary>
<ul>
<li>See full diff in <a href="https://github.com/codemirror/lang-html/commits">compare view</a></li>
</ul>
</details>
<br />

Updates `@eigenpal/docx-editor-agents` from 1.0.3 to 1.9.0
<details>
<summary>Commits</summary>
<ul>
<li>See full diff in <a href="https://github.com/eigenpal/docx-editor/commits/HEAD/packages/agents">compare view</a></li>
</ul>
</details>
<br />

Updates `@eigenpal/docx-editor-core` from 1.0.3 to 1.9.0
<details>
<summary>Commits</summary>
<ul>
<li>See full diff in <a href="https://github.com/eigenpal/docx-editor/commits/HEAD/packages/core">compare view</a></li>
</ul>
</details>
<br />

Updates `@eigenpal/docx-editor-i18n` from 1.0.3 to 1.9.0
<details>
<summary>Changelog</summary>
<p><em>Sourced from <a href="https://github.com/eigenpal/docx-editor/blob/main/packages/i18n/CHANGELOG.md">@​eigenpal/docx-editor-i18n's changelog</a>.</em></p>
<blockquote>
<h2>1.9.0</h2>
<h3>Patch Changes</h3>
<ul>
<li>28876a2: Make regular expressions over file- and library-supplied strings run in linear time and escape quoted font names completely. The variable-detection, plural-message, and core-properties date regexes no longer backtrack polynomially on hostile input, and font family names are now backslash-escaped before being wrapped in a quoted CSS string so a crafted DOCX font name cannot break out of it.</li>
</ul>
<h2>1.8.3</h2>
<h2>1.8.2</h2>
<h2>1.8.1</h2>
<h2>1.8.0</h2>
<h2>1.7.0</h2>
<h2>1.6.2</h2>
<h2>1.6.1</h2>
<h3>Patch Changes</h3>
<ul>
<li>c25ba18: Fix Indonesian (id) locale interpolation: restore the <code>{total}</code>, <code>{minRows}/{maxRows}/{minCols}/{maxCols}</code>, and <code>{label}</code> placeholders that were renamed or dropped, so the find/replace match count, insert-table validation hint, and line-spacing tooltip render their values instead of literal braces.</li>
<li>4a75c5e: Add Indonesian (id) community-maintained locale - 97% Coverage</li>
</ul>
<h2>1.6.0</h2>
<h2>1.5.0</h2>
<h2>1.4.0</h2>
<h2>1.3.3</h2>
<h2>1.3.2</h2>
<h2>1.3.1</h2>
<h2>1.3.0</h2>
<h2>1.2.1</h2>
<h2>1.2.0</h2>
<h2>1.1.0</h2>
<h3>Minor Changes</h3>
<ul>
<li>a7f9ac5: Add French locale</li>
<li>42ea72d: Track structural edits as OOXML revisions in suggesting mode. Paragraph-break insert/delete, paragraph-property changes, and table row/cell insert/delete/merge are now recorded, round-tripped through DOCX, and shown in the tracked-changes sidebar (React and Vue, localized). Adds <code>acceptChangeById(id)</code> / <code>rejectChangeById(id)</code>, and <code>acceptAllChanges</code> / <code>rejectAllChanges</code> now resolve every revision type rather than inline marks only. Fixes <a href="https://github.com/eigenpal/docx-editor/tree/HEAD/packages/i18n/issues/614">#614</a>.</li>
</ul>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li>See full diff in <a href="https://github.com/eigenpal/docx-editor/commits/HEAD/packages/i18n">compare view</a></li>
</ul>
</details>
<br />

Updates `@eigenpal/docx-editor-react` from 1.0.3 to 1.9.0
<details>
<summary>Changelog</summary>
<p><em>Sourced from <a href="https://github.com/eigenpal/docx-editor/blob/main/packages/react/CHANGELOG.md">@​eigenpal/docx-editor-react's changelog</a>.</em></p>
<blockquote>
<h2>1.9.0</h2>
<h3>Patch Changes</h3>
<ul>
<li>f61435b: Harden <code>openPrintWindow</code> to build the print window via DOM APIs instead of <code>document.write</code>, so a crafted document title cannot break out into executable markup. The framework-agnostic print helpers are now exported from <code>@docx-editor.dev/core</code> as the single source of truth, and the React package re-exports them unchanged.</li>
<li>791b132: Remove two potential slow-input denial-of-service paths in the React adapter. The data URL MIME parser now uses index math instead of a backtracking regex, and the toolbar test-id helper no longer scans across unmatched parentheses, so neither degrades on long crafted input.</li>
<li>Updated dependencies [4b47daf]</li>
<li>Updated dependencies [9144b69]</li>
<li>Updated dependencies [826aa32]</li>
<li>Updated dependencies [826aa32]</li>
<li>Updated dependencies [12c1f87]</li>
<li>Updated dependencies [7839ee9]</li>
<li>Updated dependencies [826aa32]</li>
<li>Updated dependencies [9454c9a]</li>
<li>Updated dependencies [f61435b]</li>
<li>Updated dependencies [28876a2]
<ul>
<li><a href="https://github.com/docx-editor"><code>@​docx-editor</code></a>.dev/core@1.9.0</li>
<li><a href="https://github.com/docx-editor"><code>@​docx-editor</code></a>.dev/i18n@1.9.0</li>
<li><a href="https://github.com/docx-editor"><code>@​docx-editor</code></a>.dev/agents@1.9.0</li>
</ul>
</li>
</ul>
<h2>1.8.3</h2>
<h3>Patch Changes</h3>
<ul>
<li>5ce3faa: Escape embedded font-family names before interpolating into the injected <code>@font-face</code> stylesheet, and build the print window via DOM APIs instead of <code>document.write</code> string concatenation. Prevents CSS injection and print-time XSS from crafted DOCX font names.</li>
<li>Updated dependencies [88a7650]</li>
<li>Updated dependencies [5ce3faa]</li>
<li>Updated dependencies [5eb0a43]</li>
<li>Updated dependencies [673e917]</li>
<li>Updated dependencies [74e36ef]</li>
<li>Updated dependencies [447d5b0]
<ul>
<li><a href="https://github.com/docx-editor"><code>@​docx-editor</code></a>.dev/core@1.8.3</li>
<li><a href="https://github.com/docx-editor"><code>@​docx-editor</code></a>.dev/agents@1.8.3</li>
<li><a href="https://github.com/docx-editor"><code>@​docx-editor</code></a>.dev/i18n@1.8.3</li>
</ul>
</li>
</ul>
<h2>1.8.2</h2>
<h3>Patch Changes</h3>
<ul>
<li>
<p>7811a73: Fix caret size and table insert button position when the editor is zoomed. Both are painted inside the zoomed page container, so their geometry is now normalized by the zoom factor instead of being scaled twice.</p>
<p>Fixes <a href="https://github.com/eigenpal/docx-editor/tree/HEAD/packages/react/issues/928">#928</a></p>
</li>
<li>
<p>Updated dependencies [4f183b3]</p>
</li>
<li>
<p>Updated dependencies [0c233db]</p>
</li>
<li>
<p>Updated dependencies [7811a73]</p>
<ul>
<li><a href="https://github.com/docx-editor"><code>@​docx-editor</code></a>.dev/core@1.8.2</li>
<li><a href="https://github.com/docx-editor"><code>@​docx-editor</code></a>.dev/agents@1.8.2</li>
<li><a href="https://github.com/docx-editor"><code>@​docx-editor</code></a>.dev/i18n@1.8.2</li>
</ul>
</li>
</ul>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li>See full diff in <a href="https://github.com/eigenpal/docx-editor/commits/HEAD/packages/react">compare view</a></li>
</ul>
</details>
<br />

Updates `@fortawesome/fontawesome-free` from 7.2.0 to 7.3.1
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/FortAwesome/Font-Awesome/releases">@​fortawesome/fontawesome-free's releases</a>.</em></p>
<blockquote>
<h2>Release 7.3.1</h2>
<p><strong>Change log available at <a href="https://fontawesome.com/docs/changelog/">https://fontawesome.com/docs/changelog/</a></strong></p>
<h2>Release 7.3.0</h2>
<p><strong>Change log available at <a href="https://fontawesome.com/docs/changelog/">https://fontawesome.com/docs/changelog/</a></strong></p>
</blockquote>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/FortAwesome/Font-Awesome/commit/14c65a3747d0f3b751f15831fc719236aea8729d"><code>14c65a3</code></a> Release 7.3.1 (<a href="https://redirect.github.com/FortAwesome/Font-Awesome/issues/21630">#21630</a>)</li>
<li><a href="https://github.com/FortAwesome/Font-Awesome/commit/70fb2dd154b617f62fc4ae5b0b7e2943bfd2aa96"><code>70fb2dd</code></a> Release 7.3.0 (<a href="https://redirect.github.com/FortAwesome/Font-Awesome/issues/21612">#21612</a>)</li>
<li>See full diff in <a href="https://github.com/FortAwesome/Font-Awesome/compare/7.2.0...7.3.1">compare view</a></li>
</ul>
</details>
<details>
<summary>Maintainer changes</summary>
<p>This version was pushed to npm by <a href="https://www.npmjs.com/~fortawesome-admin">fortawesome-admin</a>, a new releaser for <code>@​fortawesome/fontawesome-free</code> since your current version.</p>
</details>
<br />

Updates `@hocuspocus/provider` from 4.0.0 to 4.6.0
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/ueberdosis/hocuspocus/releases">@​hocuspocus/provider's releases</a>.</em></p>
<blockquote>
<h2>v4.6.0</h2>
<p>extension-redis will now slightly (setImmediate) delay forwarding messages to Redis, which improves performance a lot when many (500+) users are connected to the same document.</p>
<h2>What's Changed</h2>
<ul>
<li>feat/redis pending flushes by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1135">ueberdosis/hocuspocus#1135</a></li>
<li>fix: encode stateless message once when received operation via Redis … by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1136">ueberdosis/hocuspocus#1136</a></li>
</ul>
<p><strong>Full Changelog</strong>: <a href="https://github.com/ueberdosis/hocuspocus/compare/v4.5.0...v4.6.0">https://github.com/ueberdosis/hocuspocus/compare/v4.5.0...v4.6.0</a></p>
<h2>v4.5.0</h2>
<h2>What's Changed</h2>
<ul>
<li>feat: batch updates before sending to clients by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1130">ueberdosis/hocuspocus#1130</a></li>
<li>fix: ignore message in awarenessUpdateHandler if origin=this by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1129">ueberdosis/hocuspocus#1129</a></li>
<li>fix: when beforeHandleMessage throws, we don't want to process other messages that were already queued by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1123">ueberdosis/hocuspocus#1123</a></li>
</ul>
<p><strong>Full Changelog</strong>: <a href="https://github.com/ueberdosis/hocuspocus/compare/v4.4.0...v4.5.0">https://github.com/ueberdosis/hocuspocus/compare/v4.4.0...v4.5.0</a></p>
<h2>v4.4.0</h2>
<h2>What's Changed</h2>
<ul>
<li>feat: add <code>flushDelay</code> option for batching updates to reduce websocket traffic during heavy editing by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1118">ueberdosis/hocuspocus#1118</a></li>
<li>feat: add consistent state synchronization across Redis instances by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1119">ueberdosis/hocuspocus#1119</a></li>
<li>fix: make sure server.destroy() only runs once by <a href="https://github.com/DefV"><code>@​DefV</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1114">ueberdosis/hocuspocus#1114</a></li>
<li>fix: allow binding the server to a specific address by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1121">ueberdosis/hocuspocus#1121</a></li>
<li>build(deps): bump actions/checkout from 6 to 7 by <a href="https://github.com/dependabot"><code>@​dependabot</code></a>[bot] in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1117">ueberdosis/hocuspocus#1117</a></li>
<li>build(deps): bump hono from 4.12.21 to 4.12.25 by <a href="https://github.com/dependabot"><code>@​dependabot</code></a>[bot] in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1116">ueberdosis/hocuspocus#1116</a></li>
<li>build(deps): bump ws from 8.19.0 to 8.21.0 by <a href="https://github.com/dependabot"><code>@​dependabot</code></a>[bot] in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1115">ueberdosis/hocuspocus#1115</a></li>
</ul>
<h2>New Contributors</h2>
<ul>
<li><a href="https://github.com/DefV"><code>@​DefV</code></a> made their first contribution in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1114">ueberdosis/hocuspocus#1114</a></li>
</ul>
<p><strong>Full Changelog</strong>: <a href="https://github.com/ueberdosis/hocuspocus/compare/v4.3.0...v4.4.0">https://github.com/ueberdosis/hocuspocus/compare/v4.3.0...v4.4.0</a></p>
<h2>v4.3.0</h2>
<h2>What's Changed</h2>
<ul>
<li>feat: add <code>afterHandleMessage</code> hook to run after message handling completion by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1112">ueberdosis/hocuspocus#1112</a></li>
<li>feat: enforce pre-auth resource limits to safeguard server stability by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1113">ueberdosis/hocuspocus#1113</a></li>
</ul>
<p><strong>Full Changelog</strong>: <a href="https://github.com/ueberdosis/hocuspocus/compare/v4.2.0...v4.3.0">https://github.com/ueberdosis/hocuspocus/compare/v4.2.0...v4.3.0</a></p>
<h2>v4.2.0</h2>
<h2>What's Changed</h2>
<ul>
<li>feat: add <code>unloadImmediately</code> option to <code>disconnect()</code> for configurable document persistence behavior by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1111">ueberdosis/hocuspocus#1111</a></li>
</ul>
<p><strong>Full Changelog</strong>: <a href="https://github.com/ueberdosis/hocuspocus/compare/v4.1.2...v4.2.0">https://github.com/ueberdosis/hocuspocus/compare/v4.1.2...v4.2.0</a></p>
<h2>v4.1.2</h2>
<h2>What's Changed</h2>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Changelog</summary>
<p><em>Sourced from <a href="https://github.com/ueberdosis/hocuspocus/blob/main/CHANGELOG.md">@​hocuspocus/provider's changelog</a>.</em></p>
<blockquote>
<h1><a href="https://github.com/ueberdosis/hocuspocus/compare/v4.5.0...v4.6.0">4.6.0</a> (2026-08-10)</h1>
<h3>Bug Fixes</h3>
<ul>
<li>encode stateless message once when received operation via Redis ; this is a performance fix. (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1136">#1136</a>) (<a href="https://github.com/ueberdosis/hocuspocus/commit/b524b4b30299a64ffa1309f70a0fd6e761103d4a">b524b4b</a>)</li>
</ul>
<h1><a href="https://github.com/ueberdosis/hocuspocus/compare/v4.4.0...v4.5.0">4.5.0</a> (2026-08-04)</h1>
<h3>Bug Fixes</h3>
<ul>
<li>audit (<a href="https://github.com/ueberdosis/hocuspocus/commit/141360c256022deb5578c3902c3dfe0af8f6516e">141360c</a>)</li>
<li>flawky test relying on timings (<a href="https://github.com/ueberdosis/hocuspocus/commit/fe4a8e68801f1659624f53da745e595ad9f11c63">fe4a8e6</a>)</li>
<li>ignore message in awarenessUpdateHandler if origin=this (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1129">#1129</a>) (<a href="https://github.com/ueberdosis/hocuspocus/commit/08b25d4b258d932c68c999c14edcb4efc65c7a9b">08b25d4</a>)</li>
<li>update packages via audit --fix (<a href="https://github.com/ueberdosis/hocuspocus/commit/1dc9ca0ff35f1033136473d134cee8cb6b336281">1dc9ca0</a>)</li>
<li>when beforeHandleMessage throws, we don't want to process other messages that were already queued (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1123">#1123</a>) (<a href="https://github.com/ueberdosis/hocuspocus/commit/ed5dc40581cc829a6d0b04040717a8ee89296140">ed5dc40</a>)</li>
</ul>
<h3>Features</h3>
<ul>
<li>pnpm11 (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1133">#1133</a>) (<a href="https://github.com/ueberdosis/hocuspocus/commit/01c224ad9133340048c0e4f7bdce3981f4984d76">01c224a</a>)</li>
</ul>
<h1><a href="https://github.com/ueberdosis/hocuspocus/compare/v4.3.0...v4.4.0">4.4.0</a> (2026-07-13)</h1>
<h3>Bug Fixes</h3>
<ul>
<li>allow binding the server to a specific address (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1121">#1121</a>) (<a href="https://github.com/ueberdosis/hocuspocus/commit/408127b1c090356cc9148a801f314a8e6f863b09">408127b</a>)</li>
</ul>
<h3>Features</h3>
<ul>
<li>add <code>flushDelay</code> option for batching updates to reduce websocket traffic during heavy editing (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1118">#1118</a>) (<a href="https://github.com/ueberdosis/hocuspocus/commit/75594c05d57d48f2f70d4c9440c28b8226bf95ac">75594c0</a>)</li>
<li>add consistent state synchronization across Redis instances (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1119">#1119</a>) (<a href="https://github.com/ueberdosis/hocuspocus/commit/0051a6cb7618290d1f574da7ad61da2be77f839d">0051a6c</a>)</li>
</ul>
<h1><a href="https://github.com/ueberdosis/hocuspocus/compare/v4.2.0...v4.3.0">4.3.0</a> (2026-06-18)</h1>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/5c85b91af99544630200c438bfc5594a574d912e"><code>5c85b91</code></a> v4.6.0</li>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/d55367e6d3c20167d1daf920aa1e1094909a58ba"><code>d55367e</code></a> Feat/redis pending flushes (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1135">#1135</a>)</li>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/b524b4b30299a64ffa1309f70a0fd6e761103d4a"><code>b524b4b</code></a> fix: encode stateless message once when received operation via Redis ; this i...</li>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/3ec608445b8e024e15759504cca9ff1f7b09edf8"><code>3ec6084</code></a> build(deps): bump pnpm/action-setup from 5 to 6.0.9 (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1131">#1131</a>)</li>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/7827bded7c9181513a3b7c94acbaee0e4059d066"><code>7827bde</code></a> v4.5.0</li>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/141360c256022deb5578c3902c3dfe0af8f6516e"><code>141360c</code></a> fix: audit</li>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/1dc9ca0ff35f1033136473d134cee8cb6b336281"><code>1dc9ca0</code></a> fix: update packages via audit --fix</li>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/01c224ad9133340048c0e4f7bdce3981f4984d76"><code>01c224a</code></a> feat: pnpm11 (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1133">#1133</a>)</li>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/d9f87a6b738afa718dc0dd47580e02eacc764ce8"><code>d9f87a6</code></a> Feat/batch updates before sending to clients (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1130">#1130</a>)</li>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/a5812e6ec2fbeeefd6dd388a39e1d16fd192f6db"><code>a5812e6</code></a> chore: sync default port with playground</li>
<li>Additional commits viewable in <a href="https://github.com/ueberdosis/hocuspocus/compare/v4.0.0...v4.6.0">compare view</a></li>
</ul>
</details>
<br />

Updates `@mantine/charts` from 9.1.1 to 9.5.2
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/mantinedev/mantine/releases">@​mantine/charts's releases</a>.</em></p>
<blockquote>
<h2>9.5.2</h2>
<ul>
<li><code>[@mantine/hooks]</code> use-debounced-value: Fix <code>leading: true</code> firing multiple times per burst and emiting a stale value (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9119">#9119</a>)</li>
<li><code>[@mantine/schedule]</code> Fix recurring events not working with timzones (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9112">#9112</a>)</li>
<li><code>[@mantine/dates]</code> Fix <code>minDate</code> used for default date in some cases (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9117">#9117</a>)</li>
<li><code>[@mantine/core]</code> Tooltip: Fix tooltip setting NaN in top/left position style when event position values cannot be read (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9131">#9131</a>)</li>
<li><code>[@mantine/dates]</code> TimePicker: Fix incorrect focus handling of partially filled hours field (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9128">#9128</a>)</li>
<li><code>[@mantine/core]</code> RollingNumber: Fix incorrect copy event handling (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9132">#9132</a>)</li>
<li><code>[@mantine/core]</code> Notification: Fix incorrect <code>closeButtonProps</code> type (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9134">#9134</a>)</li>
<li><code>[@mantine/code-highlight]</code> Add support for lazy languages loading (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9141">#9141</a>)</li>
<li><code>[@mantine/code-highlight]</code> CodeHighlight: Add prop to keep indentation of the first line of the code block (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9140">#9140</a>)</li>
<li><code>[@mantine/dates]</code> Add missing formatting functions to MiniCalendarm DateInput and YarsList components</li>
<li><code>[@mantine/schedule]</code> WeekView: Improve performance of events positioning algorithm (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9075">#9075</a>)</li>
<li><code>[@mantine/form]</code> Add new useWatchValue hook</li>
<li><code>[@mantine/core]</code> Fix Combobox-based components not working correctly with Chrome autocomplete</li>
</ul>
<h2>9.5.1</h2>
<ul>
<li><code>[@mantine/tiptap]</code> Fix controls being initially disabledbefore element is focused</li>
<li><code>[@mantine/tiptap]</code> Fix source code control wrapping content with extra p tag</li>
<li><code>[@mantine/hooks]</code> use-scroll-spy: Allow usage with refs (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9025">#9025</a>)</li>
<li><code>[@mantine/core]</code> ColorInput: Add support for fullWidth prop (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9061">#9061</a>)</li>
<li><code>[@mantine/core]</code> Checkbox: Fix incottect indeterminate aria attributes handling in Checkbox.Card (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9095">#9095</a>)</li>
<li><code>[@mantine/core]</code> FloatingIndicator: Fix position and size calculation under scaled ancestors (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9071">#9071</a>)</li>
<li><code>[@mantine/core]</code> Tooltip: Add interactive prop support (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9072">#9072</a>)</li>
<li><code>[@mantine/core]</code> Cascader: Add safe area polygon support</li>
<li><code>[@mantine/core]</code> PasswordInput: Add option to change whether the visibility toggle is focusable (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9090">#9090</a>)</li>
<li><code>[@mantine/charts]</code> ScatterChart: Add option to add second y axis</li>
<li><code>[@mantine/schedule]</code> YearView: Add <code>renderDay</code> prop support</li>
<li><code>[@mantine/schedule]</code> YearView: Add option to hide weekend days</li>
<li><code>[@mantine/core]</code> InputWrapper: Fix <code>component: div</code> triggering typescript error if passed to <code>descriptionProps</code></li>
<li><code>[@mantine/schedule]</code> ResourcesMonthView: Add option to resize events</li>
<li><code>[@mantine/core]</code> FloatingWindow: Add support for  <code>onSizeChange</code> and <code>onResizeStart</code> props (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9085">#9085</a>)</li>
</ul>
<h2>9.5.0 🤖</h2>
<p><a href="https://mantine.dev/changelog/9-5-0">View changelog with demos on mantine.dev website</a></p>
<h2>Support Mantine development</h2>
<p>You can now sponsor Mantine development with <a href="https://opencollective.com/mantinedev">OpenCollective</a>.
All funds are used to improve Mantine and create new features and components.</p>
<h2>Migration to oxc</h2>
<p>Mantine has migrated its linting and formatting toolchain from ESLint and Prettier
to <a href="https://oxc.rs">oxc</a> – <a href="https://www.npmjs.com/package/oxlint">oxlint</a> is now used
as the linter and <a href="https://www.npmjs.com/package/oxfmt">oxfmt</a> as the formatter. Both
tools are written in Rust and are significantly faster than their predecessors, which
makes linting and formatting the entire codebase almost instant.</p>
<p>The shared configuration is available as a new
<a href="https://mantine.dev/oxc-config-mantine">oxc-config-mantine</a> package (a replacement for the previous</p>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/mantinedev/mantine/commit/8a284e2c2c53a9cb6f39f5dc389bf41b7a2073f8"><code>8a284e2</code></a> [release] Version: 9.5.2</li>
<li><a href="https://github.com/mantinedev/mantine/commit/0f57eaf5ae90c9e870fbb2a4cdd61a1d58c4c01d"><code>0f57eaf</code></a> [release] Version: 9.5.1</li>
<li><a href="https://github.com/mantinedev/mantine/commit/1e120595fdde5a414616df908bb3e600021d092e"><code>1e12059</code></a> [<code>@​mantine/charts</code>] ScatterChart: Add option to add second y axis</li>
<li><a href="https://github.com/mantinedev/mantine/commit/ca9bc6f156b63f1a10918d94ec31ec18e4e60546"><code>ca9bc6f</code></a> [release] Version: 9.5.1-alpha.1</li>
<li><a href="https://github.com/mantinedev/mantine/commit/8f1ad1bbe545c9cafafc5aef5b059d3d48e676a6"><code>8f1ad1b</code></a> [release] Version: 9.5.1-alpha.0</li>
<li><a href="https://github.com/mantinedev/mantine/commit/f1d330613f54dc9319d176e6d8ba5ebff233da18"><code>f1d3306</code></a> [release] Version: 9.5.0</li>
<li><a href="https://github.com/mantinedev/mantine/commit/732056219a0283f5822001981d7f652e632c4c87"><code>7320562</code></a> [release] Version: 9.4.3</li>
<li><a href="https://github.com/mantinedev/mantine/commit/170c45a5feed2386a464a7f05ae3daf6379cea04"><code>170c45a</code></a> Merge branch '9.5'</li>
<li><a href="https://github.com/mantinedev/mantine/commit/de21a8203060ba29441ab7623244339748e4319d"><code>de21a82</code></a> [release] Version: 9.4.3-alpha.0</li>
<li><a href="https://github.com/mantinedev/mantine/commit/e5752de4067bd58f6cdd970660b3c8469a56d4e5"><code>e5752de</code></a> [release] Version: 9.4.2</li>
<li>Additional commits viewable in <a href="https://github.com/mantinedev/mantine/commits/9.5.2/packages/@mantine/charts">compare view</a></li>
</ul>
</details>
<details>
<summary>Maintainer changes</summary>
<p>This version was pushed to npm by <a href="https://www.npmjs.com/~GitHub%20Actions">GitHub Actions</a>, a new releaser for <code>@​mantine/charts</code> since your current version.</p>
</details>
<br />

Updates `@mantine/colors-generator` from 9.1.1 to 9.5.2
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/mantinedev/mantine/releases">@​mantine/colors-generator's releases</a>.</em></p>
<blockquote>
<h2>9.5.2</h2>
<ul>
<li><code>[@mantine/hooks]</code> use-debounced-value: Fix <code>leading: true</code> firing multiple times per burst and emiting a stale value (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9119">#9119</a>)</li>
<li><code>[@mantine/schedule]</code> Fix recurring events not working with timzones (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9112">#9112</a>)</li>
<li><code>[@mantine/dates]</code> Fix <code>minDate</code> used for default date in some cases (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9117">#9117</a>)</li>
<li><code>[@mantine/core]</code> Tooltip: Fix tooltip setting NaN in top/left position style when event position values cannot be read (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9131">#9131</a>)</li>
<li><code>[@mantine/dates]</code> TimePicker: Fix incorrect focus handling of partially filled hours field (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9128">#9128</a>)</li>
<li><code>[@mantine/core]</code> RollingNumber: Fix incorrect copy event handling (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9132">#9132</a>)</li>
<li><code>[@mantine/core]</code> Notification: Fix incorrect <code>closeButtonProps</code> type (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9134">#9134</a>)</li>
<li><code>[@mantine/code-highlight]</code> Add support for lazy languages loading (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9141">#9141</a>)</li>
<li><code>[@mantine/code-highlight]</code> CodeHighlight: Add prop to keep indentation of the first line of the code block (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9140">#9140</a>)</li>
<li><code>[@mantine/dates]</code> Add missing formatting functions to MiniCalendarm DateInput and YarsList components</li>
<li><code>[@mantine/schedule]</code> WeekView: Improve performance of events positioning algorithm (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9075">#9075</a>)</li>
<li><code>[@mantine/form]</code> Add new useWatchValue hook</li>
<li><code>[@mantine/core]</code> Fix Combobox-based components not working correctly with Chrome autocomplete</li>
</ul>
<h2>9.5.1</h2>
<ul>
<li><code>[@mantine/tiptap]</code> Fix controls being initially disabledbefore element is focused</li>
<li><code>[@mantine/tiptap]</code> Fix source code control wrapping content with extra p tag</li>
<li><code>[@mantine/hooks]</code> use-scroll-spy: Allow usage with refs (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9025">#9025</a>)</li>
<li><code>[@mantine/core]</code> ColorInput: Add support for fullWidth prop (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9061">#9061</a>)</li>
<li><code>[@mantine/core]</code> Checkbox: Fix incottect indeterminate aria attributes handling in Checkbox.Card (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9095">#9095</a>)</li>
<li><code>[@mantine/core]</code> FloatingIndicator: Fix position and size calculation under scaled ancestors (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9071">#9071</a>)</li>
<li><code>[@mantine/core]</code> Tooltip: Add interactive prop support (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9072">#9072</a>)</li>
<li><code>[@mantine/core]</code> Cascader: Add safe area polygon support</li>
<li><code>[@mantine/core]</code> PasswordInput: Add option to change whether the visibility toggle is focusable (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/...

_Description has been truncated_

<details><summary>Comment — nathanpond, 2026-08-31</summary>

**Held — same blockers as the superseded archived-107** (reviewed 2026-08-31). This regrouped set still contains:
- `@blocknote/core`/`react`/`mantine` 0.51 → 0.54 — removes `YjsThreadStore` and `User` from `@blocknote/core/comments`; `src/lib/yjs/commentAudit.ts`, `useBlockNoteWithYjs.ts`, `useResolveUsers.ts` fail `tsc -b` (15 errors). Needs a code migration.
- `@eigenpal/docx-editor-*` 1.0.3 → 1.9.0 — every version of all four packages is now marked **deprecated** on npm, and 1.9.0 drops the transitive `y-prosemirror` that `DocxDocumentEditor.tsx:4` imports directly. Roadmap decision, not a bump.
- `@hocuspocus/provider` 4.0 → 4.6 must land with `@hocuspocus/server` in archived-104.

Everything else here (Mantine 9.5.2, vite 8.2.2, react 19.2.8, @xyflow, plugin-react) built cleanly in isolation on 2026-08-31, and `axios` was taken separately via archived-129. To unblock the rest, exclude `@blocknote/*`, `@eigenpal/*` and `@hocuspocus/provider` from the `spa-minor-patch` group in `.github/dependabot.yml` (or `@dependabot ignore` them) and let this PR regroup.

</details>

<details><summary>Comment — dependabot[bot], 2026-08-31</summary>

Looks like these dependencies are updatable in another way, so this is no longer needed.

</details>

---

## archived-133 — Bump ws from 8.20.1 to 8.21.3 in /src/AutoNate.Spa

`MERGED (merged 2026-08-31)` · app/dependabot · opened 2026-08-31 · `dependabot/npm_and_yarn/src/AutoNate.Spa/ws-8.21.3` → `master`

Bumps [ws](https://github.com/websockets/ws) from 8.20.1 to 8.21.3.
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/websockets/ws/releases">ws's releases</a>.</em></p>
<blockquote>
<h2>8.21.3</h2>
<h1>Bug fixes</h1>
<ul>
<li>The server now correctly rejects permessage-deflate offers if the incoming
<code>client_max_window_bits</code> parameter value is smaller than its configured
<code>clientMaxWindowBits</code> (e97a20ea).</li>
</ul>
<h2>8.21.2</h2>
<h1>Bug fixes</h1>
<ul>
<li>Fixed a test for <a href="https://github.com/nodejs/citgm">CITGM</a> (2eb3be0b).</li>
</ul>
<h2>8.21.1</h2>
<h1>Bug fixes</h1>
<ul>
<li>Empty fragments are now counted toward the limit (a2f4e7c0).</li>
<li>The default values of the <code>maxBufferedChunks</code> and <code>maxFragments</code> options have
been reduced (f197ac65).</li>
</ul>
<h2>8.21.0</h2>
<h1>Features</h1>
<ul>
<li>Introduced the <code>maxBufferedChunks</code> and <code>maxFragments</code> options (2b2abd45).</li>
</ul>
<h1>Bug fixes</h1>
<ul>
<li>Fixed a remote memory exhaustion DoS vulnerability (2b2abd45).</li>
</ul>
<p>A high volume of tiny fragments and data chunks could be sent by a peer, using
modest network traffic, to crash a <code>ws</code> server or client due to OOM.</p>
<pre lang="js"><code>import { WebSocket, WebSocketServer } from 'ws';
<p>const wss = new WebSocketServer({ port: 0 }, function () {
const data = Buffer.alloc(1);
const options = { fin: false };
const { port } = wss.address();
const ws = new WebSocket(<code>ws://localhost:${port}</code>);</p>
<p>ws.on('open', function () {
(function send() {
ws.send(data, options, function (err) {
if (err) return;
send();
});
})();
});
&lt;/tr&gt;&lt;/table&gt;
</code></pre></p>
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/websockets/ws/commit/c791e707eab3c13dd9a261d2479c3cc4a49a6fed"><code>c791e70</code></a> [dist] 8.21.3</li>
<li><a href="https://github.com/websockets/ws/commit/e97a20eaa6f2ad7969419eed732a506453251eb9"><code>e97a20e</code></a> [fix] Reject offers with <code>client_max_window_bits</code> below config</li>
<li><a href="https://github.com/websockets/ws/commit/787ebf22ce3d091fb6f931d20b4c7e914ba7cf85"><code>787ebf2</code></a> [dist] 8.21.2</li>
<li><a href="https://github.com/websockets/ws/commit/b4d62ebad40c3b925c84ff305a47975406015422"><code>b4d62eb</code></a> Revert &quot;[ci] Trust Coveralls Homebrew tap&quot;</li>
<li><a href="https://github.com/websockets/ws/commit/e4bb883723a0c18452eea10a74139901ae33c61d"><code>e4bb883</code></a> [security] Use GitHub PVR as main reporting channel</li>
<li><a href="https://github.com/websockets/ws/commit/2eb3be0bff2453e2654b1315c5872e8d5d424a50"><code>2eb3be0</code></a> [test] Skip test on Node.js versions where it does not apply</li>
<li><a href="https://github.com/websockets/ws/commit/ae1de54330cef77e487548890fabfeb9aae1d83d"><code>ae1de54</code></a> [dist] 8.21.1</li>
<li><a href="https://github.com/websockets/ws/commit/8e9511b86b3fc6deebbd97dd9af7c9056deea8d1"><code>8e9511b</code></a> [ci] Trust Coveralls Homebrew tap</li>
<li><a href="https://github.com/websockets/ws/commit/f197ac65140920bdcecdab74bfc69c2d7858e55d"><code>f197ac6</code></a> [fix] Lower default values of <code>maxBufferedChunks</code> and <code>maxFragments</code></li>
<li><a href="https://github.com/websockets/ws/commit/8df8265c2f63fd44af3193a98e23cf38888cd991"><code>8df8265</code></a> [ci] Update actions/checkout action to v7</li>
<li>Additional commits viewable in <a href="https://github.com/websockets/ws/compare/8.20.1...8.21.3">compare view</a></li>
</ul>
</details>
<br />


[![Dependabot compatibility score](https://dependabot-badges.githubapp.com/badges/compatibility_score?dependency-name=ws&package-manager=npm_and_yarn&previous-version=8.20.1&new-version=8.21.3)](https://docs.github.com/en/github/managing-security-vulnerabilities/about-dependabot-security-updates#about-compatibility-scores)

Dependabot will resolve any conflicts with this PR as long as you don't alter it yourself. You can also trigger a rebase manually by commenting `@dependabot rebase`.

[//]: # (dependabot-automerge-start)
[//]: # (dependabot-automerge-end)

---

<details>
<summary>Dependabot commands and options</summary>
<br />

You can trigger Dependabot actions by commenting on this PR:
- `@dependabot rebase` will rebase this PR
- `@dependabot recreate` will recreate this PR, overwriting any edits that have been made to it
- `@dependabot show <dependency name> ignore conditions` will show all of the ignore conditions of the specified dependency
- `@dependabot ignore this major version` will close this PR and stop Dependabot creating any more for this major version (unless you reopen the PR or upgrade to it yourself)
- `@dependabot ignore this minor version` will close this PR and stop Dependabot creating any more for this minor version (unless you reopen the PR or upgrade to it yourself)
- `@dependabot ignore this dependency` will close this PR and stop Dependabot creating any more for this dependency (unless you reopen the PR or upgrade to it yourself)
You can disable automated security fix PRs for this repo from the [Security Alerts page](https://github.com/nathanpond/AutoNate/network/alerts).

</details>

---

## archived-134 — Bump form-data from 4.0.5 to 4.0.6 in /src/AutoNate.Spa

`MERGED (merged 2026-08-31)` · app/dependabot · opened 2026-08-31 · `dependabot/npm_and_yarn/src/AutoNate.Spa/form-data-4.0.6` → `master`

Bumps [form-data](https://github.com/form-data/form-data) from 4.0.5 to 4.0.6.
<details>
<summary>Changelog</summary>
<p><em>Sourced from <a href="https://github.com/form-data/form-data/blob/master/CHANGELOG.md">form-data's changelog</a>.</em></p>
<blockquote>
<h2><a href="https://github.com/form-data/form-data/compare/v4.0.5...v4.0.6">v4.0.6</a> - 2026-06-12</h2>
<h3>Commits</h3>
<ul>
<li>[Fix] escape CR, LF, and <code>&quot;</code> in field names and filenames <a href="https://github.com/form-data/form-data/commit/8dff42c6da654ed4e7ad4acb7f8ccd3831217c99"><code>8dff42c</code></a></li>
<li>[Dev Deps] update <code>@ljharb/eslint-config</code>, <code>auto-changelog</code>, <code>tape</code> <a href="https://github.com/form-data/form-data/commit/f31d21ef10bf46e46344c3ee4f99acbef6be43e1"><code>f31d21e</code></a></li>
<li>[Deps] update <code>hasown</code>, <code>mime-types</code> <a href="https://github.com/form-data/form-data/commit/92ae0eb5da94d6f01925d5f4fcffb2a1e50ed7cd"><code>92ae0eb</code></a></li>
<li>[Dev Deps] update <code>js-randomness-predictor</code> <a href="https://github.com/form-data/form-data/commit/67b0f65c2e0b065a511d42227d35e4d367644e97"><code>67b0f65</code></a></li>
</ul>
</blockquote>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/form-data/form-data/commit/64190db548c0179e37206858e39f27cf513e9435"><code>64190db</code></a> v4.0.6</li>
<li><a href="https://github.com/form-data/form-data/commit/92ae0eb5da94d6f01925d5f4fcffb2a1e50ed7cd"><code>92ae0eb</code></a> [Deps] update <code>hasown</code>, <code>mime-types</code></li>
<li><a href="https://github.com/form-data/form-data/commit/f31d21ef10bf46e46344c3ee4f99acbef6be43e1"><code>f31d21e</code></a> [Dev Deps] update <code>@ljharb/eslint-config</code>, <code>auto-changelog</code>, <code>tape</code></li>
<li><a href="https://github.com/form-data/form-data/commit/8dff42c6da654ed4e7ad4acb7f8ccd3831217c99"><code>8dff42c</code></a> [Fix] escape CR, LF, and <code>&quot;</code> in field names and filenames</li>
<li><a href="https://github.com/form-data/form-data/commit/67b0f65c2e0b065a511d42227d35e4d367644e97"><code>67b0f65</code></a> [Dev Deps] update <code>js-randomness-predictor</code></li>
<li>See full diff in <a href="https://github.com/form-data/form-data/compare/v4.0.5...v4.0.6">compare view</a></li>
</ul>
</details>
<br />


[![Dependabot compatibility score](https://dependabot-badges.githubapp.com/badges/compatibility_score?dependency-name=form-data&package-manager=npm_and_yarn&previous-version=4.0.5&new-version=4.0.6)](https://docs.github.com/en/github/managing-security-vulnerabilities/about-dependabot-security-updates#about-compatibility-scores)

Dependabot will resolve any conflicts with this PR as long as you don't alter it yourself. You can also trigger a rebase manually by commenting `@dependabot rebase`.

[//]: # (dependabot-automerge-start)
[//]: # (dependabot-automerge-end)

---

<details>
<summary>Dependabot commands and options</summary>
<br />

You can trigger Dependabot actions by commenting on this PR:
- `@dependabot rebase` will rebase this PR
- `@dependabot recreate` will recreate this PR, overwriting any edits that have been made to it
- `@dependabot show <dependency name> ignore conditions` will show all of the ignore conditions of the specified dependency
- `@dependabot ignore this major version` will close this PR and stop Dependabot creating any more for this major version (unless you reopen the PR or upgrade to it yourself)
- `@dependabot ignore this minor version` will close this PR and stop Dependabot creating any more for this minor version (unless you reopen the PR or upgrade to it yourself)
- `@dependabot ignore this dependency` will close this PR and stop Dependabot creating any more for this dependency (unless you reopen the PR or upgrade to it yourself)
You can disable automated security fix PRs for this repo from the [Security Alerts page](https://github.com/nathanpond/AutoNate/network/alerts).

</details>

---

## archived-135 — Bump vite from 8.0.10 to 8.2.2 in /src/AutoNate.Spa

`MERGED (merged 2026-08-31)` · app/dependabot · opened 2026-08-31 · `dependabot/npm_and_yarn/src/AutoNate.Spa/vite-8.2.2` → `master`

Bumps [vite](https://github.com/vitejs/vite/tree/HEAD/packages/vite) from 8.0.10 to 8.2.2.
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/vitejs/vite/releases">vite's releases</a>.</em></p>
<blockquote>
<h2>plugin-legacy@8.2.2</h2>
<p>Please refer to <a href="https://github.com/vitejs/vite/blob/plugin-legacy@8.2.2/packages/plugin-legacy/CHANGELOG.md">CHANGELOG.md</a> for details.</p>
<h2>v8.2.2</h2>
<p>Please refer to <a href="https://github.com/vitejs/vite/blob/v8.2.2/packages/vite/CHANGELOG.md">CHANGELOG.md</a> for details.</p>
<h2>plugin-legacy@8.2.1</h2>
<p>Please refer to <a href="https://github.com/vitejs/vite/blob/plugin-legacy@8.2.1/packages/plugin-legacy/CHANGELOG.md">CHANGELOG.md</a> for details.</p>
<h2>v8.2.1</h2>
<p>Please refer to <a href="https://github.com/vitejs/vite/blob/v8.2.1/packages/vite/CHANGELOG.md">CHANGELOG.md</a> for details.</p>
<h2>create-vite@8.2.0</h2>
<p>Please refer to <a href="https://github.com/vitejs/vite/blob/create-vite@8.2.0/packages/create-vite/CHANGELOG.md">CHANGELOG.md</a> for details.</p>
<h2>plugin-legacy@8.2.0</h2>
<p>Please refer to <a href="https://github.com/vitejs/vite/blob/plugin-legacy@8.2.0/packages/plugin-legacy/CHANGELOG.md">CHANGELOG.md</a> for details.</p>
<h2>v8.2.0</h2>
<p>Please refer to <a href="https://github.com/vitejs/vite/blob/v8.2.0/packages/vite/CHANGELOG.md">CHANGELOG.md</a> for details.</p>
<h2>v8.2.0-beta.0</h2>
<p>Please refer to <a href="https://github.com/vitejs/vite/blob/v8.2.0-beta.0/packages/vite/CHANGELOG.md">CHANGELOG.md</a> for details.</p>
<h2>v8.1.5</h2>
<p>Please refer to <a href="https://github.com/vitejs/vite/blob/v8.1.5/packages/vite/CHANGELOG.md">CHANGELOG.md</a> for details.</p>
<h2>v8.1.4</h2>
<p>Please refer to <a href="https://github.com/vitejs/vite/blob/v8.1.4/packages/vite/CHANGELOG.md">CHANGELOG.md</a> for details.</p>
<h2>v8.1.3</h2>
<p>Please refer to <a href="https://github.com/vitejs/vite/blob/v8.1.3/packages/vite/CHANGELOG.md">CHANGELOG.md</a> for details.</p>
<h2>v8.1.2</h2>
<p>Please refer to <a href="https://github.com/vitejs/vite/blob/v8.1.2/packages/vite/CHANGELOG.md">CHANGELOG.md</a> for details.</p>
<h2>v8.1.1</h2>
<p>Please refer to <a href="https://github.com/vitejs/vite/blob/v8.1.1/packages/vite/CHANGELOG.md">CHANGELOG.md</a> for details.</p>
<h2>create-vite@8.1.0</h2>
<p>Please refer to <a href="https://github.com/vitejs/vite/blob/create-vite@8.1.0/packages/create-vite/CHANGELOG.md">CHANGELOG.md</a> for details.</p>
<h2>plugin-legacy@8.1.0</h2>
<p>Please refer to <a href="https://github.com/vitejs/vite/blob/plugin-legacy@8.1.0/packages/plugin-legacy/CHANGELOG.md">CHANGELOG.md</a> for details.</p>
<h2>v8.1.0</h2>
<p>Please refer to <a href="https://github.com/vitejs/vite/blob/v8.1.0/packages/vite/CHANGELOG.md">CHANGELOG.md</a> for details.</p>
<h2>plugin-legacy@8.1.0-beta.0</h2>
<p>Please refer to <a href="https://github.com/vitejs/vite/blob/plugin-legacy@8.1.0-beta.0/packages/plugin-legacy/CHANGELOG.md">CHANGELOG.md</a> for details.</p>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Changelog</summary>
<p><em>Sourced from <a href="https://github.com/vitejs/vite/blob/main/packages/vite/CHANGELOG.md">vite's changelog</a>.</em></p>
<blockquote>
<h2><!-- raw HTML omitted --><a href="https://github.com/vitejs/vite/compare/v8.2.1...v8.2.2">8.2.2</a> (2026-08-20)<!-- raw HTML omitted --></h2>
<h3>Features</h3>
<ul>
<li><strong>deps:</strong> widen <code>@vitejs/devtools</code> peer range to v0.5.0 (<a href="https://redirect.github.com/vitejs/vite/issues/23302">#23302</a>) (<a href="https://github.com/vitejs/vite/commit/495d9ff5a7d843ca876a9e49799947a5deb704c7">495d9ff</a>)</li>
</ul>
<h3>Bug Fixes</h3>
<ul>
<li><strong>bundled-dev:</strong> handle lazy request error (<a href="https://redirect.github.com/vitejs/vite/issues/23291">#23291</a>) (<a href="https://github.com/vitejs/vite/commit/3ba026dade4af56df08815310d3458fa110f5c5c">3ba026d</a>)</li>
<li><strong>bundled-dev:</strong> hot update through circular imports instead of reloading (<a href="https://redirect.github.com/vitejs/vite/issues/23259">#23259</a>) (<a href="https://github.com/vitejs/vite/commit/3dbddefaafc091a879b06f9279296f776691e455">3dbddef</a>)</li>
<li><strong>config:</strong> resolve sourcemap paths against sourcemap location (<a href="https://redirect.github.com/vitejs/vite/issues/23239">#23239</a>) (<a href="https://github.com/vitejs/vite/commit/05a003e6a17a84d75f907ea0f1598bc39b8dce6c">05a003e</a>)</li>
<li><strong>css:</strong> don't pass empty targets to lightningcss (<a href="https://redirect.github.com/vitejs/vite/issues/23295">#23295</a>) (<a href="https://github.com/vitejs/vite/commit/2804636ff608d105928009d274ffba7cfbe55340">2804636</a>)</li>
<li><strong>define:</strong> fix match escaped dots to support $-prefixed define keys (<a href="https://redirect.github.com/vitejs/vite/issues/23249">#23249</a>) (<a href="https://github.com/vitejs/vite/commit/dcf88bd2ad2b1a8845f9029587cc8c825e382d42">dcf88bd</a>)</li>
<li><strong>deps:</strong> update all non-major dependencies (<a href="https://redirect.github.com/vitejs/vite/issues/23217">#23217</a>) (<a href="https://github.com/vitejs/vite/commit/ba958bddfc9cabe302c6b34269dcf5c9634531e0">ba958bd</a>)</li>
<li><strong>deps:</strong> update rolldown-related dependencies (<a href="https://redirect.github.com/vitejs/vite/issues/23218">#23218</a>) (<a href="https://github.com/vitejs/vite/commit/83ecb2c8059e8ce946a7cc835d4c14ef78aef4fd">83ecb2c</a>)</li>
<li><strong>module-runner:</strong> exclude completed modules from in-flight cycle detection (fix <a href="https://redirect.github.com/vitejs/vite/issues/22999">#22999</a>) (<a href="https://redirect.github.com/vitejs/vite/issues/23009">#23009</a>) (<a href="https://github.com/vitejs/vite/commit/d9b10a98db1c293ee64300bd75d568b44c8ae931">d9b10a9</a>)</li>
<li><strong>optimizer:</strong> close custom extension analysis bundles (<a href="https://redirect.github.com/vitejs/vite/issues/23207">#23207</a>) (<a href="https://github.com/vitejs/vite/commit/8fb76752836f61224d3095b502fa237b478a06b2">8fb7675</a>)</li>
<li>reduce Windows 8.3-short-name detection false-positives (<a href="https://redirect.github.com/vitejs/vite/issues/23066">#23066</a>) (<a href="https://github.com/vitejs/vite/commit/02cffa9e2d38d5d8f12e4043ee9d0f7abb1471e2">02cffa9</a>)</li>
<li>respect <code>resolve.preserveSymlinks</code> when resolving root (fix <a href="https://redirect.github.com/vitejs/vite/issues/23197">#23197</a>) (<a href="https://redirect.github.com/vitejs/vite/issues/23198">#23198</a>) (<a href="https://github.com/vitejs/vite/commit/8413052731836d4aaf3eb94a0f25788dd35d2888">8413052</a>)</li>
<li><strong>ssr:</strong> rewrite computed key of destructing parameter (<a href="https://redirect.github.com/vitejs/vite/issues/23307">#23307</a>) (<a href="https://github.com/vitejs/vite/commit/9db0b61d4c9c7caad7ea1d9670b637faf2bb6c93">9db0b61</a>)</li>
<li><strong>vite:</strong> update outdated upstream file links in license comments (<a href="https://redirect.github.com/vitejs/vite/issues/23285">#23285</a>) (<a href="https://github.com/vitejs/vite/commit/c0f2fc607ee97ee4499337b04826420c00654065">c0f2fc6</a>)</li>
</ul>
<h3>Documentation</h3>
<ul>
<li><strong>build:</strong> note cssTarget precedence (<a href="https://redirect.github.com/vitejs/vite/issues/23200">#23200</a>) (<a href="https://github.com/vitejs/vite/commit/a20a35ec0685e374519864d0f41dd5f6e9ba0271">a20a35e</a>)</li>
</ul>
<h3>Miscellaneous Chores</h3>
<ul>
<li>fix ts errors in build test cases (<a href="https://redirect.github.com/vitejs/vite/issues/23209">#23209</a>) (<a href="https://github.com/vitejs/vite/commit/a0cfcf72f8ef8bf0f2f11d553333b9bb31f1d316">a0cfcf7</a>)</li>
</ul>
<h3>Code Refactoring</h3>
<ul>
<li>use JSON import attributes instead of readFileSync in constants (<a href="https://redirect.github.com/vitejs/vite/issues/23258">#23258</a>) (<a href="https://github.com/vitejs/vite/commit/1d9fa392a43229241f80630236f8552ce8f7cd0f">1d9fa39</a>)</li>
<li>use named regex constants over inline literals (<a href="https://redirect.github.com/vitejs/vite/issues/22964">#22964</a>) (<a href="https://github.com/vitejs/vite/commit/5c1c6c609718303202832f706884192e1f1e9223">5c1c6c6</a>)</li>
</ul>
<h3>Tests</h3>
<ul>
<li><strong>define:</strong> close rolldown bundler after generate (<a href="https://redirect.github.com/vitejs/vite/issues/23231">#23231</a>) (<a href="https://github.com/vitejs/vite/commit/b4d66fee14d970f45b8a6f3d7d6aee73ca9b88ab">b4d66fe</a>)</li>
<li><strong>module-runner:</strong> add TLA circular import case (<a href="https://redirect.github.com/vitejs/vite/issues/23299">#23299</a>) (<a href="https://github.com/vitejs/vite/commit/4a261f242831bef92afd2f1aacfb81eab9dec371">4a261f2</a>)</li>
<li><strong>module-runner:</strong> simplify server-hmr tests (<a href="https://redirect.github.com/vitejs/vite/issues/23300">#23300</a>) (<a href="https://github.com/vitejs/vite/commit/599b44b6600ec426e10cd556908d53b027b0c4fb">599b44b</a>)</li>
<li><strong>ssr:</strong> add destructing assignment case for moduleRunnerTransform (<a href="https://redirect.github.com/vitejs/vite/issues/23308">#23308</a>) (<a href="https://github.com/vitejs/vite/commit/cb77e2a93bad2a8ece00b4aa0ef507c092582c45">cb77e2a</a>)</li>
</ul>
<h3>Build System</h3>
<ul>
<li>use JSON import attributes instead of readFIleSync in rolldown configs (<a href="https://redirect.github.com/vitejs/vite/issues/23251">#23251</a>) (<a href="https://github.com/vitejs/vite/commit/d615bcdb23d96c1ca5ce1ee45e21d8d87381106f">d615bcd</a>)</li>
</ul>
<h2><!-- raw HTML omitted --><a href="https://github.com/vitejs/vite/compare/v8.2.0...v8.2.1">8.2.1</a> (2026-08-06)<!-- raw HTML omitted --></h2>
<h3>Bug Fixes</h3>
<ul>
<li><strong>build:</strong> make client chunkImportMap work with <code>sharedPlugins: true</code> (<a href="https://redirect.github.com/vitejs/vite/issues/23184">#23184</a>) (<a href="https://github.com/vitejs/vite/commit/15f03073c915d6ffb9a1fda447ef66b02bf5cde8">15f0307</a>)</li>
<li><strong>bundled-dev:</strong> inject client script tag before chunk scripts (<a href="https://redirect.github.com/vitejs/vite/issues/23161">#23161</a>) (<a href="https://github.com/vitejs/vite/commit/eac0cc84aa2472a85a19ee84561c1ba71e381a55">eac0cc8</a>)</li>
</ul>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/vitejs/vite/commit/de1111ab0be00879b404e7ed3b2a80e264edddc1"><code>de1111a</code></a> release: v8.2.2</li>
<li><a href="https://github.com/vitejs/vite/commit/cb77e2a93bad2a8ece00b4aa0ef507c092582c45"><code>cb77e2a</code></a> test(ssr): add destructing assignment case for moduleRunnerTransform (<a href="https://github.com/vitejs/vite/tree/HEAD/packages/vite/issues/23308">#23308</a>)</li>
<li><a href="https://github.com/vitejs/vite/commit/9db0b61d4c9c7caad7ea1d9670b637faf2bb6c93"><code>9db0b61</code></a> fix(ssr): rewrite computed key of destructing parameter (<a href="https://github.com/vitejs/vite/tree/HEAD/packages/vite/issues/23307">#23307</a>)</li>
<li><a href="https://github.com/vitejs/vite/commit/8413052731836d4aaf3eb94a0f25788dd35d2888"><code>8413052</code></a> fix: respect <code>resolve.preserveSymlinks</code> when resolving root (fix <a href="https://github.com/vitejs/vite/tree/HEAD/packages/vite/issues/23197">#23197</a>) (<a href="https://github.com/vitejs/vite/tree/HEAD/packages/vite/issues/23">#23</a>...</li>
<li><a href="https://github.com/vitejs/vite/commit/05a003e6a17a84d75f907ea0f1598bc39b8dce6c"><code>05a003e</code></a> fix(config): resolve sourcemap paths against sourcemap location (<a href="https://github.com/vitejs/vite/tree/HEAD/packages/vite/issues/23239">#23239</a>)</li>
<li><a href="https://github.com/vitejs/vite/commit/495d9ff5a7d843ca876a9e49799947a5deb704c7"><code>495d9ff</code></a> feat(deps): widen <code>@vitejs/devtools</code> peer range to v0.5.0 (<a href="https://github.com/vitejs/vite/tree/HEAD/packages/vite/issues/23302">#23302</a>)</li>
<li><a href="https://github.com/vitejs/vite/commit/1d9fa392a43229241f80630236f8552ce8f7cd0f"><code>1d9fa39</code></a> refactor: use JSON import attributes instead of readFileSync in constants (<a href="https://github.com/vitejs/vite/tree/HEAD/packages/vite/issues/2">#2</a>...</li>
<li><a href="https://github.com/vitejs/vite/commit/2804636ff608d105928009d274ffba7cfbe55340"><code>2804636</code></a> fix(css): don't pass empty targets to lightningcss (<a href="https://github.com/vitejs/vite/tree/HEAD/packages/vite/issues/23295">#23295</a>)</li>
<li><a href="https://github.com/vitejs/vite/commit/599b44b6600ec426e10cd556908d53b027b0c4fb"><code>599b44b</code></a> test(module-runner): simplify server-hmr tests (<a href="https://github.com/vitejs/vite/tree/HEAD/packages/vite/issues/23300">#23300</a>)</li>
<li><a href="https://github.com/vitejs/vite/commit/4a261f242831bef92afd2f1aacfb81eab9dec371"><code>4a261f2</code></a> test(module-runner): add TLA circular import case (<a href="https://github.com/vitejs/vite/tree/HEAD/packages/vite/issues/23299">#23299</a>)</li>
<li>Additional commits viewable in <a href="https://github.com/vitejs/vite/commits/v8.2.2/packages/vite">compare view</a></li>
</ul>
</details>
<br />


[![Dependabot compatibility score](https://dependabot-badges.githubapp.com/badges/compatibility_score?dependency-name=vite&package-manager=npm_and_yarn&previous-version=8.0.10&new-version=8.2.2)](https://docs.github.com/en/github/managing-security-vulnerabilities/about-dependabot-security-updates#about-compatibility-scores)

Dependabot will resolve any conflicts with this PR as long as you don't alter it yourself. You can also trigger a rebase manually by commenting `@dependabot rebase`.

[//]: # (dependabot-automerge-start)
[//]: # (dependabot-automerge-end)

---

<details>
<summary>Dependabot commands and options</summary>
<br />

You can trigger Dependabot actions by commenting on this PR:
- `@dependabot rebase` will rebase this PR
- `@dependabot recreate` will recreate this PR, overwriting any edits that have been made to it
- `@dependabot show <dependency name> ignore conditions` will show all of the ignore conditions of the specified dependency
- `@dependabot ignore this major version` will close this PR and stop Dependabot creating any more for this major version (unless you reopen the PR or upgrade to it yourself)
- `@dependabot ignore this minor version` will close this PR and stop Dependabot creating any more for this minor version (unless you reopen the PR or upgrade to it yourself)
- `@dependabot ignore this dependency` will close this PR and stop Dependabot creating any more for this dependency (unless you reopen the PR or upgrade to it yourself)
You can disable automated security fix PRs for this repo from the [Security Alerts page](https://github.com/nathanpond/AutoNate/network/alerts).

</details>

---

## archived-136 — Serve the SPA shell at the site root again; fail-fast E2E fixture

`MERGED (merged 2026-08-31)` · nathanpond · opened 2026-08-31 · `fix/132-e2e-harness` → `master`

Closes archived-132

## What
`bdc72176` constrained the SPA fallback to `{*path:nonfile:regex(^(?!api(/|$)))}` so `/api` can never fall through to `index.html`. `RegexRouteConstraint` returns `false` for a missing catch-all value, so the root URL `/` stopped matching — deep links still served the shell, `/` was a bare 404. Every Playwright spec starts at `/`, so the whole E2E suite turned into identical 30 s sign-in timeouts with no diagnostic. In a Release deploy the same bug hits every user who opens the site root.

## Changes
- `Program.cs`: explicit `MapFallbackToFile("/", "index.html")` next to the constrained catch-all (comment explains why both are needed).
- `SpaRootFallbackTests` (new, `AutoNate.Web.Tests`): boots the host with a throw-away `wwwroot`; asserts `/` and deep links serve the shell, `/api/*` is 404 and never the shell, static files serve. **Fails on exactly the `/` case with the fix removed** (verified).
- `AutoNateWebApplicationFactory`: optional `webRoot` so tests can reach the static-file/fallback pipeline that is skipped when `wwwroot/` is absent.
- `AutoNateE2EFixture`: after `Now listening`, probes `GET /` and throws with the app's stdout/stderr tail unless the SPA shell is served.
- `docs/codebase/Testing.md`: browser-install prerequisite for bare `dotnet test` and the two new guards.

## Test plan
- [x] `dotnet test tests/AutoNate.Web.Tests --filter SpaRootFallbackTests` → 6/6
- [x] Same with the fix reverted → 1 failure, `SpaRoutes_ServeTheShell(path: "/")`
- [x] `dotnet test tests/AutoNate.E2E.Tests --filter RecordsCrudTests|ManageUsersTests|NotificationsTests` → **Passed! Failed: 0, Passed: 14, Total: 14, Duration: 12 s** (same slice was 14/14 timeouts before the fix)

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Y5ie3qTEptr4MjYw5i6a5F

---

## archived-137 — Bump form-data from 4.0.5 to 4.0.6 in /services/hocuspocus

`MERGED (merged 2026-08-31)` · app/dependabot · opened 2026-08-31 · `dependabot/npm_and_yarn/services/hocuspocus/form-data-4.0.6` → `master`

Bumps [form-data](https://github.com/form-data/form-data) from 4.0.5 to 4.0.6.
<details>
<summary>Changelog</summary>
<p><em>Sourced from <a href="https://github.com/form-data/form-data/blob/master/CHANGELOG.md">form-data's changelog</a>.</em></p>
<blockquote>
<h2><a href="https://github.com/form-data/form-data/compare/v4.0.5...v4.0.6">v4.0.6</a> - 2026-06-12</h2>
<h3>Commits</h3>
<ul>
<li>[Fix] escape CR, LF, and <code>&quot;</code> in field names and filenames <a href="https://github.com/form-data/form-data/commit/8dff42c6da654ed4e7ad4acb7f8ccd3831217c99"><code>8dff42c</code></a></li>
<li>[Dev Deps] update <code>@ljharb/eslint-config</code>, <code>auto-changelog</code>, <code>tape</code> <a href="https://github.com/form-data/form-data/commit/f31d21ef10bf46e46344c3ee4f99acbef6be43e1"><code>f31d21e</code></a></li>
<li>[Deps] update <code>hasown</code>, <code>mime-types</code> <a href="https://github.com/form-data/form-data/commit/92ae0eb5da94d6f01925d5f4fcffb2a1e50ed7cd"><code>92ae0eb</code></a></li>
<li>[Dev Deps] update <code>js-randomness-predictor</code> <a href="https://github.com/form-data/form-data/commit/67b0f65c2e0b065a511d42227d35e4d367644e97"><code>67b0f65</code></a></li>
</ul>
</blockquote>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/form-data/form-data/commit/64190db548c0179e37206858e39f27cf513e9435"><code>64190db</code></a> v4.0.6</li>
<li><a href="https://github.com/form-data/form-data/commit/92ae0eb5da94d6f01925d5f4fcffb2a1e50ed7cd"><code>92ae0eb</code></a> [Deps] update <code>hasown</code>, <code>mime-types</code></li>
<li><a href="https://github.com/form-data/form-data/commit/f31d21ef10bf46e46344c3ee4f99acbef6be43e1"><code>f31d21e</code></a> [Dev Deps] update <code>@ljharb/eslint-config</code>, <code>auto-changelog</code>, <code>tape</code></li>
<li><a href="https://github.com/form-data/form-data/commit/8dff42c6da654ed4e7ad4acb7f8ccd3831217c99"><code>8dff42c</code></a> [Fix] escape CR, LF, and <code>&quot;</code> in field names and filenames</li>
<li><a href="https://github.com/form-data/form-data/commit/67b0f65c2e0b065a511d42227d35e4d367644e97"><code>67b0f65</code></a> [Dev Deps] update <code>js-randomness-predictor</code></li>
<li>See full diff in <a href="https://github.com/form-data/form-data/compare/v4.0.5...v4.0.6">compare view</a></li>
</ul>
</details>
<br />


[![Dependabot compatibility score](https://dependabot-badges.githubapp.com/badges/compatibility_score?dependency-name=form-data&package-manager=npm_and_yarn&previous-version=4.0.5&new-version=4.0.6)](https://docs.github.com/en/github/managing-security-vulnerabilities/about-dependabot-security-updates#about-compatibility-scores)

Dependabot will resolve any conflicts with this PR as long as you don't alter it yourself. You can also trigger a rebase manually by commenting `@dependabot rebase`.

[//]: # (dependabot-automerge-start)
[//]: # (dependabot-automerge-end)

---

<details>
<summary>Dependabot commands and options</summary>
<br />

You can trigger Dependabot actions by commenting on this PR:
- `@dependabot rebase` will rebase this PR
- `@dependabot recreate` will recreate this PR, overwriting any edits that have been made to it
- `@dependabot show <dependency name> ignore conditions` will show all of the ignore conditions of the specified dependency
- `@dependabot ignore this major version` will close this PR and stop Dependabot creating any more for this major version (unless you reopen the PR or upgrade to it yourself)
- `@dependabot ignore this minor version` will close this PR and stop Dependabot creating any more for this minor version (unless you reopen the PR or upgrade to it yourself)
- `@dependabot ignore this dependency` will close this PR and stop Dependabot creating any more for this dependency (unless you reopen the PR or upgrade to it yourself)
You can disable automated security fix PRs for this repo from the [Security Alerts page](https://github.com/nathanpond/AutoNate/network/alerts).

</details>

---

## archived-138 — Bump the spa-minor-patch group across 1 directory with 37 updates

`CLOSED` · app/dependabot · opened 2026-08-31 · `dependabot/npm_and_yarn/src/AutoNate.Spa/spa-minor-patch-9197407e85` → `master`

Bumps the spa-minor-patch group with 37 updates in the /src/AutoNate.Spa directory:

| Package | From | To |
| --- | --- | --- |
| [@blocknote/core](https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/core) | `0.51.0` | `0.54.0` |
| [@blocknote/mantine](https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/mantine) | `0.51.0` | `0.54.0` |
| [@blocknote/react](https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/react) | `0.51.0` | `0.54.0` |
| [@codemirror/lang-html](https://github.com/codemirror/lang-html) | `6.4.11` | `6.4.12` |
| [@eigenpal/docx-editor-agents](https://github.com/eigenpal/docx-editor/tree/HEAD/packages/agents) | `1.0.3` | `1.9.0` |
| [@eigenpal/docx-editor-core](https://github.com/eigenpal/docx-editor/tree/HEAD/packages/core) | `1.0.3` | `1.9.0` |
| [@eigenpal/docx-editor-i18n](https://github.com/eigenpal/docx-editor/tree/HEAD/packages/i18n) | `1.0.3` | `1.9.0` |
| [@eigenpal/docx-editor-react](https://github.com/eigenpal/docx-editor/tree/HEAD/packages/react) | `1.0.3` | `1.9.0` |
| [@fortawesome/fontawesome-free](https://github.com/FortAwesome/Font-Awesome) | `7.2.0` | `7.3.1` |
| [@hocuspocus/provider](https://github.com/ueberdosis/hocuspocus) | `4.0.0` | `4.6.0` |
| [@mantine/charts](https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts) | `9.1.1` | `9.5.2` |
| [@mantine/colors-generator](https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator) | `9.1.1` | `9.5.2` |
| [@mantine/core](https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/core) | `9.1.1` | `9.5.2` |
| [@mantine/dates](https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dates) | `9.1.1` | `9.5.2` |
| [@mantine/dropzone](https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dropzone) | `9.1.1` | `9.5.2` |
| [@mantine/form](https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/form) | `9.1.1` | `9.5.2` |
| [@mantine/hooks](https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/hooks) | `9.1.1` | `9.5.2` |
| [@mantine/modals](https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/modals) | `9.1.1` | `9.5.2` |
| [@mantine/notifications](https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/notifications) | `9.1.1` | `9.5.2` |
| [@tanstack/react-query](https://github.com/TanStack/query/tree/HEAD/packages/react-query) | `5.100.1` | `5.102.8` |
| [@tanstack/react-query-devtools](https://github.com/TanStack/query/tree/HEAD/packages/react-query-devtools) | `5.100.1` | `5.102.8` |
| [@uiw/react-codemirror](https://github.com/uiwjs/react-codemirror) | `4.25.9` | `4.25.11` |
| [@xyflow/react](https://github.com/xyflow/xyflow/tree/HEAD/packages/react) | `12.10.2` | `12.11.5` |
| [axios](https://github.com/axios/axios) | `1.18.0` | `1.20.0` |
| [marked](https://github.com/markedjs/marked) | `18.0.4` | `18.0.11` |
| [react](https://github.com/react/react/tree/HEAD/packages/react) | `19.2.5` | `19.2.8` |
| [@types/react](https://github.com/DefinitelyTyped/DefinitelyTyped/tree/HEAD/types/react) | `19.2.14` | `19.2.18` |
| [react-dom](https://github.com/react/react/tree/HEAD/packages/react-dom) | `19.2.5` | `19.2.8` |
| [@types/react-dom](https://github.com/DefinitelyTyped/DefinitelyTyped/tree/HEAD/types/react-dom) | `19.2.3` | `19.2.5` |
| [react-grid-layout](https://github.com/STRML/react-grid-layout) | `2.2.3` | `2.2.4` |
| [@types/react-grid-layout](https://github.com/DefinitelyTyped/DefinitelyTyped/tree/HEAD/types/react-grid-layout) | `1.3.6` | `2.1.0` |
| [recharts](https://github.com/recharts/recharts) | `3.8.1` | `3.10.1` |
| [yjs](https://github.com/yjs/yjs) | `13.6.30` | `13.6.32` |
| [zod](https://github.com/colinhacks/zod) | `4.3.6` | `4.4.3` |
| [@vitejs/plugin-react](https://github.com/vitejs/vite-plugin-react/tree/HEAD/packages/plugin-react) | `6.0.1` | `6.1.1` |
| [globals](https://github.com/sindresorhus/globals) | `17.6.0` | `17.11.0` |
| [typescript-eslint](https://github.com/typescript-eslint/typescript-eslint/tree/HEAD/packages/typescript-eslint) | `8.60.0` | `8.68.0` |


Updates `@blocknote/core` from 0.51.0 to 0.54.0
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/TypeCellOS/BlockNote/releases">@​blocknote/core's releases</a>.</em></p>
<blockquote>
<h2>v0.54.0</h2>
<h2>0.54.0 (2026-08-13)</h2>
<p>💖 The math block and diagram block has been sponsored by <a href="https://www.numerique.gouv.fr/dinum/">DINUM</a> 🇫🇷</p>
<h3>Math Block</h3>
<p>A long requested feature, you can now add block &amp; inline math to a BlockNote editor. They are driven by <a href="https://katex.org/">Katex</a> &amp; support much of <a href="https://www.latex-project.org/">Latex</a> for all your notation needs.</p>
<p><a href="https://github.com/user-attachments/assets/8fb5790e-6922-4f02-a35f-27c791b877e8">https://github.com/user-attachments/assets/8fb5790e-6922-4f02-a35f-27c791b877e8</a></p>
<p><a href="https://www.blocknotejs.org/examples/custom-schema/math-block">Link to demo</a></p>
<h3>Diagram Block</h3>
<p>We've also added support for a diagram block driven by <a href="https://mermaid.js.org/">Mermaid.js</a>, allowing you to add diagramming to the editor.</p>
<p><a href="https://github.com/user-attachments/assets/0a64e98a-5bf0-4dec-b1a4-84ccf98f4a70">https://github.com/user-attachments/assets/0a64e98a-5bf0-4dec-b1a4-84ccf98f4a70</a></p>
<p><a href="https://www.blocknotejs.org/examples/custom-schema/diagram-block">Link to demo</a></p>
<h3>Source Block with Preview</h3>
<p>Both the Math block &amp; Diagram block are built on a primitive that you can build your own custom blocks from. The Source Block with Preview primitive allows you to build a pair of a block which renders content with an inline editor for the content being rendered. This can enable other sorts of preview-like features in the future, exposed as an API for you to build your own custom blocks with.</p>
<!-- raw HTML omitted -->
<!-- raw HTML omitted -->
<p><a href="https://www.blocknotejs.org/examples/custom-schema/source-with-preview">Link to demo</a></p>
<h3>🚀 Features</h3>
<ul>
<li>Adds a Math block (<a href="https://github.com/TypeCellOS/BlockNote/commit/2a34f7d70">2a34f7d70</a>)</li>
<li>Adds a Diagram block (<a href="https://github.com/TypeCellOS/BlockNote/commit/0fca0ee7a">0fca0ee7a</a>)</li>
<li><strong>core:</strong> Source-with-preview, syntax highlighting &amp; exporter images (<a href="https://github.com/TypeCellOS/BlockNote/commit/503c796d3">503c796d3</a>)</li>
</ul>
<h3>🩹 Fixes</h3>
<ul>
<li><strong>ai:</strong> Operations on collaborative documents (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2952">#2952</a>)</li>
<li><strong>ai:</strong> Operations on blocks containing comments (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2953">#2953</a>)</li>
<li><strong>pdf:</strong> Add custom font and fontFamily options for CJK (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2945">#2945</a>)</li>
<li>Expose first suggestion as active descendant (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2965">#2965</a>)</li>
<li><strong>xl-docx-exporter:</strong> Clamp list nesting to the levels DOCX defines (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2969">#2969</a>)</li>
</ul>
<h3>❤️ Thank You</h3>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Changelog</summary>
<p><em>Sourced from <a href="https://github.com/TypeCellOS/BlockNote/blob/main/CHANGELOG.md">@​blocknote/core's changelog</a>.</em></p>
<blockquote>
<h2>0.54.0 (2026-08-13)</h2>
<h3>🚀 Features</h3>
<ul>
<li>Adds a Math block (<a href="https://github.com/TypeCellOS/BlockNote/commit/2a34f7d70">2a34f7d70</a>)</li>
<li>Adds a Diagram block (<a href="https://github.com/TypeCellOS/BlockNote/commit/0fca0ee7a">0fca0ee7a</a>)</li>
<li><strong>core:</strong> Source-with-preview, syntax highlighting &amp; exporter images (<a href="https://github.com/TypeCellOS/BlockNote/commit/503c796d3">503c796d3</a>)</li>
</ul>
<h3>🩹 Fixes</h3>
<ul>
<li><strong>ai:</strong> Operations on collaborative documents (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2952">#2952</a>)</li>
<li><strong>ai:</strong> Operations on blocks containing comments (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2953">#2953</a>)</li>
<li><strong>pdf:</strong> Add custom font and fontFamily options for CJK (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2945">#2945</a>)</li>
<li>Expose first suggestion as active descendant (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2965">#2965</a>)</li>
<li><strong>xl-docx-exporter:</strong> Clamp list nesting to the levels DOCX defines (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2969">#2969</a>)</li>
</ul>
<h3>❤️ Thank You</h3>
<ul>
<li>Adarshsm <a href="mailto:adarshmudugal@gmail.com">adarshmudugal@gmail.com</a></li>
<li>Nick The Sick (<a href="https://github.com/nperez0111"><code>@​nperez0111</code></a>)</li>
<li>Pupuking723 <a href="mailto:2318857637@qq.com">2318857637@qq.com</a></li>
</ul>
<h2>0.53.0 (2026-08-06)</h2>
<h3>🚀 Features</h3>
<ul>
<li><strong>shadcn:</strong> ⚠️ Use base-ui instead of radix (BLO-1279) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2913">#2913</a>)</li>
</ul>
<h3>🩹 Fixes</h3>
<ul>
<li>getCellSelection throwing error in positions (BLO-1193) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2911">#2911</a>)</li>
<li>Multi-column slash menu items within a column (BLO-905) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2914">#2914</a>)</li>
<li>Suggestion menu behaviour (BLO-1283, BLO-955) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2930">#2930</a>)</li>
<li>Ignore useless block/inline content mutations (BLO-1224) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2912">#2912</a>)</li>
<li><strong>slash-menu:</strong> Better overflow behavior (BLO-1192) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2909">#2909</a>)</li>
<li>Slash menu item selection behaviour (BLO-1222) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2838">#2838</a>)</li>
<li>HTML export/parse round trip ignoring empty blocks (BLO-873) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2931">#2931</a>)</li>
<li><strong>core:</strong> Guard getBlock() calls to prevent TypeError on stale blocks (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2941">#2941</a>)</li>
<li>Stop stale node view positions crashing the editor (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2938">#2938</a>)</li>
<li>Multi-column trailing blocks, column hover borders &amp; drop cursor left edge BLO-1226 (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2885">#2885</a>)</li>
</ul>
<h4>⚠️ Breaking Changes</h4>
<ul>
<li><strong>shadcn:</strong> ⚠️ Use base-ui instead of radix (BLO-1279) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2913">#2913</a>)</li>
</ul>
<h3>❤️ Thank You</h3>
<ul>
<li>Yousef</li>
<li>Nick Perez <a href="mailto:nick@blocknotejs.org">nick@blocknotejs.org</a></li>
<li>Matthew Lipski (<a href="https://github.com/matthewlipski"><code>@​matthewlipski</code></a>)</li>
</ul>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/ea5d80358f179d1683abcd2e0e3e9d547bf52eef"><code>ea5d803</code></a> chore(release): v0.54.0</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/503c796d37f2c8734cf65e9bad3348127043c63b"><code>503c796</code></a> feat(core): source-with-preview, syntax highlighting &amp; exporter images</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/99253c3814a93e6f5d1ae318efeb0b10df90f32d"><code>99253c3</code></a> chore: migrate to TypeScript 7 and consolidate the <a href="https://github.com/shared"><code>@​shared</code></a> alias</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/bea469e31eab19242b1238cd3600a14c1d6148c1"><code>bea469e</code></a> refactor: vendor <code>@​tanstack/store</code> as a first-party Store (<a href="https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/core/issues/2956">#2956</a>)</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/dee3401a2647eb01b7a982b32e98e0bd182713fe"><code>dee3401</code></a> chore: bump prosemirror-view to ^1.42.2 (<a href="https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/core/issues/2954">#2954</a>)</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/decb3d21480ceed983d3befb4e87ff8d26bcc938"><code>decb3d2</code></a> fix(ai): operations on blocks containing comments (<a href="https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/core/issues/2953">#2953</a>)</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/824abce757ed1a44e4dbb048fe88ea954b592831"><code>824abce</code></a> fix(ai): operations on collaborative documents (<a href="https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/core/issues/2952">#2952</a>)</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/529c3b02f6e413c362e96718dd712dd4b4c495a0"><code>529c3b0</code></a> chore(release): v0.53.0</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/d998f0168abd54ec57239479ea2dfc3d17df6a1a"><code>d998f01</code></a> fix: multi-column trailing blocks, column hover borders &amp; drop cursor left ed...</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/58d43ff08806ce078f03cf5a28afeefb1bede482"><code>58d43ff</code></a> fix: stop stale node view positions crashing the editor (<a href="https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/core/issues/2938">#2938</a>)</li>
<li>Additional commits viewable in <a href="https://github.com/TypeCellOS/BlockNote/commits/v0.54.0/packages/core">compare view</a></li>
</ul>
</details>
<br />

Updates `@blocknote/mantine` from 0.51.0 to 0.54.0
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/TypeCellOS/BlockNote/releases">@​blocknote/mantine's releases</a>.</em></p>
<blockquote>
<h2>v0.54.0</h2>
<h2>0.54.0 (2026-08-13)</h2>
<p>💖 The math block and diagram block has been sponsored by <a href="https://www.numerique.gouv.fr/dinum/">DINUM</a> 🇫🇷</p>
<h3>Math Block</h3>
<p>A long requested feature, you can now add block &amp; inline math to a BlockNote editor. They are driven by <a href="https://katex.org/">Katex</a> &amp; support much of <a href="https://www.latex-project.org/">Latex</a> for all your notation needs.</p>
<p><a href="https://github.com/user-attachments/assets/8fb5790e-6922-4f02-a35f-27c791b877e8">https://github.com/user-attachments/assets/8fb5790e-6922-4f02-a35f-27c791b877e8</a></p>
<p><a href="https://www.blocknotejs.org/examples/custom-schema/math-block">Link to demo</a></p>
<h3>Diagram Block</h3>
<p>We've also added support for a diagram block driven by <a href="https://mermaid.js.org/">Mermaid.js</a>, allowing you to add diagramming to the editor.</p>
<p><a href="https://github.com/user-attachments/assets/0a64e98a-5bf0-4dec-b1a4-84ccf98f4a70">https://github.com/user-attachments/assets/0a64e98a-5bf0-4dec-b1a4-84ccf98f4a70</a></p>
<p><a href="https://www.blocknotejs.org/examples/custom-schema/diagram-block">Link to demo</a></p>
<h3>Source Block with Preview</h3>
<p>Both the Math block &amp; Diagram block are built on a primitive that you can build your own custom blocks from. The Source Block with Preview primitive allows you to build a pair of a block which renders content with an inline editor for the content being rendered. This can enable other sorts of preview-like features in the future, exposed as an API for you to build your own custom blocks with.</p>
<!-- raw HTML omitted -->
<!-- raw HTML omitted -->
<p><a href="https://www.blocknotejs.org/examples/custom-schema/source-with-preview">Link to demo</a></p>
<h3>🚀 Features</h3>
<ul>
<li>Adds a Math block (<a href="https://github.com/TypeCellOS/BlockNote/commit/2a34f7d70">2a34f7d70</a>)</li>
<li>Adds a Diagram block (<a href="https://github.com/TypeCellOS/BlockNote/commit/0fca0ee7a">0fca0ee7a</a>)</li>
<li><strong>core:</strong> Source-with-preview, syntax highlighting &amp; exporter images (<a href="https://github.com/TypeCellOS/BlockNote/commit/503c796d3">503c796d3</a>)</li>
</ul>
<h3>🩹 Fixes</h3>
<ul>
<li><strong>ai:</strong> Operations on collaborative documents (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2952">#2952</a>)</li>
<li><strong>ai:</strong> Operations on blocks containing comments (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2953">#2953</a>)</li>
<li><strong>pdf:</strong> Add custom font and fontFamily options for CJK (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2945">#2945</a>)</li>
<li>Expose first suggestion as active descendant (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2965">#2965</a>)</li>
<li><strong>xl-docx-exporter:</strong> Clamp list nesting to the levels DOCX defines (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2969">#2969</a>)</li>
</ul>
<h3>❤️ Thank You</h3>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Changelog</summary>
<p><em>Sourced from <a href="https://github.com/TypeCellOS/BlockNote/blob/main/CHANGELOG.md">@​blocknote/mantine's changelog</a>.</em></p>
<blockquote>
<h2>0.54.0 (2026-08-13)</h2>
<h3>🚀 Features</h3>
<ul>
<li>Adds a Math block (<a href="https://github.com/TypeCellOS/BlockNote/commit/2a34f7d70">2a34f7d70</a>)</li>
<li>Adds a Diagram block (<a href="https://github.com/TypeCellOS/BlockNote/commit/0fca0ee7a">0fca0ee7a</a>)</li>
<li><strong>core:</strong> Source-with-preview, syntax highlighting &amp; exporter images (<a href="https://github.com/TypeCellOS/BlockNote/commit/503c796d3">503c796d3</a>)</li>
</ul>
<h3>🩹 Fixes</h3>
<ul>
<li><strong>ai:</strong> Operations on collaborative documents (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2952">#2952</a>)</li>
<li><strong>ai:</strong> Operations on blocks containing comments (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2953">#2953</a>)</li>
<li><strong>pdf:</strong> Add custom font and fontFamily options for CJK (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2945">#2945</a>)</li>
<li>Expose first suggestion as active descendant (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2965">#2965</a>)</li>
<li><strong>xl-docx-exporter:</strong> Clamp list nesting to the levels DOCX defines (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2969">#2969</a>)</li>
</ul>
<h3>❤️ Thank You</h3>
<ul>
<li>Adarshsm <a href="mailto:adarshmudugal@gmail.com">adarshmudugal@gmail.com</a></li>
<li>Nick The Sick (<a href="https://github.com/nperez0111"><code>@​nperez0111</code></a>)</li>
<li>Pupuking723 <a href="mailto:2318857637@qq.com">2318857637@qq.com</a></li>
</ul>
<h2>0.53.0 (2026-08-06)</h2>
<h3>🚀 Features</h3>
<ul>
<li><strong>shadcn:</strong> ⚠️ Use base-ui instead of radix (BLO-1279) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2913">#2913</a>)</li>
</ul>
<h3>🩹 Fixes</h3>
<ul>
<li>getCellSelection throwing error in positions (BLO-1193) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2911">#2911</a>)</li>
<li>Multi-column slash menu items within a column (BLO-905) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2914">#2914</a>)</li>
<li>Suggestion menu behaviour (BLO-1283, BLO-955) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2930">#2930</a>)</li>
<li>Ignore useless block/inline content mutations (BLO-1224) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2912">#2912</a>)</li>
<li><strong>slash-menu:</strong> Better overflow behavior (BLO-1192) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2909">#2909</a>)</li>
<li>Slash menu item selection behaviour (BLO-1222) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2838">#2838</a>)</li>
<li>HTML export/parse round trip ignoring empty blocks (BLO-873) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2931">#2931</a>)</li>
<li><strong>core:</strong> Guard getBlock() calls to prevent TypeError on stale blocks (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2941">#2941</a>)</li>
<li>Stop stale node view positions crashing the editor (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2938">#2938</a>)</li>
<li>Multi-column trailing blocks, column hover borders &amp; drop cursor left edge BLO-1226 (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2885">#2885</a>)</li>
</ul>
<h4>⚠️ Breaking Changes</h4>
<ul>
<li><strong>shadcn:</strong> ⚠️ Use base-ui instead of radix (BLO-1279) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2913">#2913</a>)</li>
</ul>
<h3>❤️ Thank You</h3>
<ul>
<li>Yousef</li>
<li>Nick Perez <a href="mailto:nick@blocknotejs.org">nick@blocknotejs.org</a></li>
<li>Matthew Lipski (<a href="https://github.com/matthewlipski"><code>@​matthewlipski</code></a>)</li>
</ul>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/ea5d80358f179d1683abcd2e0e3e9d547bf52eef"><code>ea5d803</code></a> chore(release): v0.54.0</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/99253c3814a93e6f5d1ae318efeb0b10df90f32d"><code>99253c3</code></a> chore: migrate to TypeScript 7 and consolidate the <a href="https://github.com/shared"><code>@​shared</code></a> alias</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/529c3b02f6e413c362e96718dd712dd4b4c495a0"><code>529c3b0</code></a> chore(release): v0.53.0</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/47d864c6e997963281af4df5ec54a4421773c134"><code>47d864c</code></a> fix(slash-menu): better overflow behavior (BLO-1192) (<a href="https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/mantine/issues/2909">#2909</a>)</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/8288b926e8a34737f287da1310e709b4785e2461"><code>8288b92</code></a> style: grid suggestion menu item padding (BLO-1225) (<a href="https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/mantine/issues/2910">#2910</a>)</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/dee7880b89b1e9bc00b4f4481f32652c7a4b4408"><code>dee7880</code></a> chore(release): v0.52.1</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/a99aab441b5db07c35d9f5ce406ea1676c6314ca"><code>a99aab4</code></a> chore(release): v0.52.0</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/030dcf0d133d99a173b8fa44ceec11b07a82867e"><code>030dcf0</code></a> refactor(versioning): consolidate sidebar CSS into the shared stylesheet</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/ef34ecca53f6d4c6a3cb0fa4d1058424e9a9124f"><code>ef34ecc</code></a> refactor(ui): forward refs in AttributionTooltip implementations</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/161a6147c09b81a0fc5af97afcc8606111481e4a"><code>161a614</code></a> fix(versioning): make yhub history snapshot ids unique and fix grouping</li>
<li>Additional commits viewable in <a href="https://github.com/TypeCellOS/BlockNote/commits/v0.54.0/packages/mantine">compare view</a></li>
</ul>
</details>
<br />

Updates `@blocknote/react` from 0.51.0 to 0.54.0
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/TypeCellOS/BlockNote/releases">@​blocknote/react's releases</a>.</em></p>
<blockquote>
<h2>v0.54.0</h2>
<h2>0.54.0 (2026-08-13)</h2>
<p>💖 The math block and diagram block has been sponsored by <a href="https://www.numerique.gouv.fr/dinum/">DINUM</a> 🇫🇷</p>
<h3>Math Block</h3>
<p>A long requested feature, you can now add block &amp; inline math to a BlockNote editor. They are driven by <a href="https://katex.org/">Katex</a> &amp; support much of <a href="https://www.latex-project.org/">Latex</a> for all your notation needs.</p>
<p><a href="https://github.com/user-attachments/assets/8fb5790e-6922-4f02-a35f-27c791b877e8">https://github.com/user-attachments/assets/8fb5790e-6922-4f02-a35f-27c791b877e8</a></p>
<p><a href="https://www.blocknotejs.org/examples/custom-schema/math-block">Link to demo</a></p>
<h3>Diagram Block</h3>
<p>We've also added support for a diagram block driven by <a href="https://mermaid.js.org/">Mermaid.js</a>, allowing you to add diagramming to the editor.</p>
<p><a href="https://github.com/user-attachments/assets/0a64e98a-5bf0-4dec-b1a4-84ccf98f4a70">https://github.com/user-attachments/assets/0a64e98a-5bf0-4dec-b1a4-84ccf98f4a70</a></p>
<p><a href="https://www.blocknotejs.org/examples/custom-schema/diagram-block">Link to demo</a></p>
<h3>Source Block with Preview</h3>
<p>Both the Math block &amp; Diagram block are built on a primitive that you can build your own custom blocks from. The Source Block with Preview primitive allows you to build a pair of a block which renders content with an inline editor for the content being rendered. This can enable other sorts of preview-like features in the future, exposed as an API for you to build your own custom blocks with.</p>
<!-- raw HTML omitted -->
<!-- raw HTML omitted -->
<p><a href="https://www.blocknotejs.org/examples/custom-schema/source-with-preview">Link to demo</a></p>
<h3>🚀 Features</h3>
<ul>
<li>Adds a Math block (<a href="https://github.com/TypeCellOS/BlockNote/commit/2a34f7d70">2a34f7d70</a>)</li>
<li>Adds a Diagram block (<a href="https://github.com/TypeCellOS/BlockNote/commit/0fca0ee7a">0fca0ee7a</a>)</li>
<li><strong>core:</strong> Source-with-preview, syntax highlighting &amp; exporter images (<a href="https://github.com/TypeCellOS/BlockNote/commit/503c796d3">503c796d3</a>)</li>
</ul>
<h3>🩹 Fixes</h3>
<ul>
<li><strong>ai:</strong> Operations on collaborative documents (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2952">#2952</a>)</li>
<li><strong>ai:</strong> Operations on blocks containing comments (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2953">#2953</a>)</li>
<li><strong>pdf:</strong> Add custom font and fontFamily options for CJK (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2945">#2945</a>)</li>
<li>Expose first suggestion as active descendant (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2965">#2965</a>)</li>
<li><strong>xl-docx-exporter:</strong> Clamp list nesting to the levels DOCX defines (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2969">#2969</a>)</li>
</ul>
<h3>❤️ Thank You</h3>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Changelog</summary>
<p><em>Sourced from <a href="https://github.com/TypeCellOS/BlockNote/blob/main/CHANGELOG.md">@​blocknote/react's changelog</a>.</em></p>
<blockquote>
<h2>0.54.0 (2026-08-13)</h2>
<h3>🚀 Features</h3>
<ul>
<li>Adds a Math block (<a href="https://github.com/TypeCellOS/BlockNote/commit/2a34f7d70">2a34f7d70</a>)</li>
<li>Adds a Diagram block (<a href="https://github.com/TypeCellOS/BlockNote/commit/0fca0ee7a">0fca0ee7a</a>)</li>
<li><strong>core:</strong> Source-with-preview, syntax highlighting &amp; exporter images (<a href="https://github.com/TypeCellOS/BlockNote/commit/503c796d3">503c796d3</a>)</li>
</ul>
<h3>🩹 Fixes</h3>
<ul>
<li><strong>ai:</strong> Operations on collaborative documents (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2952">#2952</a>)</li>
<li><strong>ai:</strong> Operations on blocks containing comments (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2953">#2953</a>)</li>
<li><strong>pdf:</strong> Add custom font and fontFamily options for CJK (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2945">#2945</a>)</li>
<li>Expose first suggestion as active descendant (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2965">#2965</a>)</li>
<li><strong>xl-docx-exporter:</strong> Clamp list nesting to the levels DOCX defines (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2969">#2969</a>)</li>
</ul>
<h3>❤️ Thank You</h3>
<ul>
<li>Adarshsm <a href="mailto:adarshmudugal@gmail.com">adarshmudugal@gmail.com</a></li>
<li>Nick The Sick (<a href="https://github.com/nperez0111"><code>@​nperez0111</code></a>)</li>
<li>Pupuking723 <a href="mailto:2318857637@qq.com">2318857637@qq.com</a></li>
</ul>
<h2>0.53.0 (2026-08-06)</h2>
<h3>🚀 Features</h3>
<ul>
<li><strong>shadcn:</strong> ⚠️ Use base-ui instead of radix (BLO-1279) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2913">#2913</a>)</li>
</ul>
<h3>🩹 Fixes</h3>
<ul>
<li>getCellSelection throwing error in positions (BLO-1193) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2911">#2911</a>)</li>
<li>Multi-column slash menu items within a column (BLO-905) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2914">#2914</a>)</li>
<li>Suggestion menu behaviour (BLO-1283, BLO-955) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2930">#2930</a>)</li>
<li>Ignore useless block/inline content mutations (BLO-1224) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2912">#2912</a>)</li>
<li><strong>slash-menu:</strong> Better overflow behavior (BLO-1192) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2909">#2909</a>)</li>
<li>Slash menu item selection behaviour (BLO-1222) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2838">#2838</a>)</li>
<li>HTML export/parse round trip ignoring empty blocks (BLO-873) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2931">#2931</a>)</li>
<li><strong>core:</strong> Guard getBlock() calls to prevent TypeError on stale blocks (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2941">#2941</a>)</li>
<li>Stop stale node view positions crashing the editor (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2938">#2938</a>)</li>
<li>Multi-column trailing blocks, column hover borders &amp; drop cursor left edge BLO-1226 (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2885">#2885</a>)</li>
</ul>
<h4>⚠️ Breaking Changes</h4>
<ul>
<li><strong>shadcn:</strong> ⚠️ Use base-ui instead of radix (BLO-1279) (<a href="https://redirect.github.com/TypeCellOS/BlockNote/pull/2913">#2913</a>)</li>
</ul>
<h3>❤️ Thank You</h3>
<ul>
<li>Yousef</li>
<li>Nick Perez <a href="mailto:nick@blocknotejs.org">nick@blocknotejs.org</a></li>
<li>Matthew Lipski (<a href="https://github.com/matthewlipski"><code>@​matthewlipski</code></a>)</li>
</ul>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/ea5d80358f179d1683abcd2e0e3e9d547bf52eef"><code>ea5d803</code></a> chore(release): v0.54.0</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/503c796d37f2c8734cf65e9bad3348127043c63b"><code>503c796</code></a> feat(core): source-with-preview, syntax highlighting &amp; exporter images</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/99253c3814a93e6f5d1ae318efeb0b10df90f32d"><code>99253c3</code></a> chore: migrate to TypeScript 7 and consolidate the <a href="https://github.com/shared"><code>@​shared</code></a> alias</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/115d4333660a15391eea073ac7e7dd3ddb9da69a"><code>115d433</code></a> fix: expose first suggestion as active descendant (<a href="https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/react/issues/2965">#2965</a>)</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/bea469e31eab19242b1238cd3600a14c1d6148c1"><code>bea469e</code></a> refactor: vendor <code>@​tanstack/store</code> as a first-party Store (<a href="https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/react/issues/2956">#2956</a>)</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/529c3b02f6e413c362e96718dd712dd4b4c495a0"><code>529c3b0</code></a> chore(release): v0.53.0</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/d998f0168abd54ec57239479ea2dfc3d17df6a1a"><code>d998f01</code></a> fix: multi-column trailing blocks, column hover borders &amp; drop cursor left ed...</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/58d43ff08806ce078f03cf5a28afeefb1bede482"><code>58d43ff</code></a> fix: stop stale node view positions crashing the editor (<a href="https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/react/issues/2938">#2938</a>)</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/c32f9680082dc57c4bb2782a424ac67574a5713c"><code>c32f968</code></a> fix(core): guard getBlock() calls to prevent TypeError on stale blocks (<a href="https://github.com/TypeCellOS/BlockNote/tree/HEAD/packages/react/issues/2941">#2941</a>)</li>
<li><a href="https://github.com/TypeCellOS/BlockNote/commit/dee7880b89b1e9bc00b4f4481f32652c7a4b4408"><code>dee7880</code></a> chore(release): v0.52.1</li>
<li>Additional commits viewable in <a href="https://github.com/TypeCellOS/BlockNote/commits/v0.54.0/packages/react">compare view</a></li>
</ul>
</details>
<br />

Updates `@codemirror/lang-html` from 6.4.11 to 6.4.12
<details>
<summary>Commits</summary>
<ul>
<li>See full diff in <a href="https://github.com/codemirror/lang-html/commits">compare view</a></li>
</ul>
</details>
<br />

Updates `@eigenpal/docx-editor-agents` from 1.0.3 to 1.9.0
<details>
<summary>Commits</summary>
<ul>
<li>See full diff in <a href="https://github.com/eigenpal/docx-editor/commits/HEAD/packages/agents">compare view</a></li>
</ul>
</details>
<br />

Updates `@eigenpal/docx-editor-core` from 1.0.3 to 1.9.0
<details>
<summary>Commits</summary>
<ul>
<li>See full diff in <a href="https://github.com/eigenpal/docx-editor/commits/HEAD/packages/core">compare view</a></li>
</ul>
</details>
<br />

Updates `@eigenpal/docx-editor-i18n` from 1.0.3 to 1.9.0
<details>
<summary>Changelog</summary>
<p><em>Sourced from <a href="https://github.com/eigenpal/docx-editor/blob/main/packages/i18n/CHANGELOG.md">@​eigenpal/docx-editor-i18n's changelog</a>.</em></p>
<blockquote>
<h2>1.9.0</h2>
<h3>Patch Changes</h3>
<ul>
<li>28876a2: Make regular expressions over file- and library-supplied strings run in linear time and escape quoted font names completely. The variable-detection, plural-message, and core-properties date regexes no longer backtrack polynomially on hostile input, and font family names are now backslash-escaped before being wrapped in a quoted CSS string so a crafted DOCX font name cannot break out of it.</li>
</ul>
<h2>1.8.3</h2>
<h2>1.8.2</h2>
<h2>1.8.1</h2>
<h2>1.8.0</h2>
<h2>1.7.0</h2>
<h2>1.6.2</h2>
<h2>1.6.1</h2>
<h3>Patch Changes</h3>
<ul>
<li>c25ba18: Fix Indonesian (id) locale interpolation: restore the <code>{total}</code>, <code>{minRows}/{maxRows}/{minCols}/{maxCols}</code>, and <code>{label}</code> placeholders that were renamed or dropped, so the find/replace match count, insert-table validation hint, and line-spacing tooltip render their values instead of literal braces.</li>
<li>4a75c5e: Add Indonesian (id) community-maintained locale - 97% Coverage</li>
</ul>
<h2>1.6.0</h2>
<h2>1.5.0</h2>
<h2>1.4.0</h2>
<h2>1.3.3</h2>
<h2>1.3.2</h2>
<h2>1.3.1</h2>
<h2>1.3.0</h2>
<h2>1.2.1</h2>
<h2>1.2.0</h2>
<h2>1.1.0</h2>
<h3>Minor Changes</h3>
<ul>
<li>a7f9ac5: Add French locale</li>
<li>42ea72d: Track structural edits as OOXML revisions in suggesting mode. Paragraph-break insert/delete, paragraph-property changes, and table row/cell insert/delete/merge are now recorded, round-tripped through DOCX, and shown in the tracked-changes sidebar (React and Vue, localized). Adds <code>acceptChangeById(id)</code> / <code>rejectChangeById(id)</code>, and <code>acceptAllChanges</code> / <code>rejectAllChanges</code> now resolve every revision type rather than inline marks only. Fixes <a href="https://github.com/eigenpal/docx-editor/tree/HEAD/packages/i18n/issues/614">#614</a>.</li>
</ul>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li>See full diff in <a href="https://github.com/eigenpal/docx-editor/commits/HEAD/packages/i18n">compare view</a></li>
</ul>
</details>
<br />

Updates `@eigenpal/docx-editor-react` from 1.0.3 to 1.9.0
<details>
<summary>Changelog</summary>
<p><em>Sourced from <a href="https://github.com/eigenpal/docx-editor/blob/main/packages/react/CHANGELOG.md">@​eigenpal/docx-editor-react's changelog</a>.</em></p>
<blockquote>
<h2>1.9.0</h2>
<h3>Patch Changes</h3>
<ul>
<li>f61435b: Harden <code>openPrintWindow</code> to build the print window via DOM APIs instead of <code>document.write</code>, so a crafted document title cannot break out into executable markup. The framework-agnostic print helpers are now exported from <code>@docx-editor.dev/core</code> as the single source of truth, and the React package re-exports them unchanged.</li>
<li>791b132: Remove two potential slow-input denial-of-service paths in the React adapter. The data URL MIME parser now uses index math instead of a backtracking regex, and the toolbar test-id helper no longer scans across unmatched parentheses, so neither degrades on long crafted input.</li>
<li>Updated dependencies [4b47daf]</li>
<li>Updated dependencies [9144b69]</li>
<li>Updated dependencies [826aa32]</li>
<li>Updated dependencies [826aa32]</li>
<li>Updated dependencies [12c1f87]</li>
<li>Updated dependencies [7839ee9]</li>
<li>Updated dependencies [826aa32]</li>
<li>Updated dependencies [9454c9a]</li>
<li>Updated dependencies [f61435b]</li>
<li>Updated dependencies [28876a2]
<ul>
<li><a href="https://github.com/docx-editor"><code>@​docx-editor</code></a>.dev/core@1.9.0</li>
<li><a href="https://github.com/docx-editor"><code>@​docx-editor</code></a>.dev/i18n@1.9.0</li>
<li><a href="https://github.com/docx-editor"><code>@​docx-editor</code></a>.dev/agents@1.9.0</li>
</ul>
</li>
</ul>
<h2>1.8.3</h2>
<h3>Patch Changes</h3>
<ul>
<li>5ce3faa: Escape embedded font-family names before interpolating into the injected <code>@font-face</code> stylesheet, and build the print window via DOM APIs instead of <code>document.write</code> string concatenation. Prevents CSS injection and print-time XSS from crafted DOCX font names.</li>
<li>Updated dependencies [88a7650]</li>
<li>Updated dependencies [5ce3faa]</li>
<li>Updated dependencies [5eb0a43]</li>
<li>Updated dependencies [673e917]</li>
<li>Updated dependencies [74e36ef]</li>
<li>Updated dependencies [447d5b0]
<ul>
<li><a href="https://github.com/docx-editor"><code>@​docx-editor</code></a>.dev/core@1.8.3</li>
<li><a href="https://github.com/docx-editor"><code>@​docx-editor</code></a>.dev/agents@1.8.3</li>
<li><a href="https://github.com/docx-editor"><code>@​docx-editor</code></a>.dev/i18n@1.8.3</li>
</ul>
</li>
</ul>
<h2>1.8.2</h2>
<h3>Patch Changes</h3>
<ul>
<li>
<p>7811a73: Fix caret size and table insert button position when the editor is zoomed. Both are painted inside the zoomed page container, so their geometry is now normalized by the zoom factor instead of being scaled twice.</p>
<p>Fixes <a href="https://github.com/eigenpal/docx-editor/tree/HEAD/packages/react/issues/928">#928</a></p>
</li>
<li>
<p>Updated dependencies [4f183b3]</p>
</li>
<li>
<p>Updated dependencies [0c233db]</p>
</li>
<li>
<p>Updated dependencies [7811a73]</p>
<ul>
<li><a href="https://github.com/docx-editor"><code>@​docx-editor</code></a>.dev/core@1.8.2</li>
<li><a href="https://github.com/docx-editor"><code>@​docx-editor</code></a>.dev/agents@1.8.2</li>
<li><a href="https://github.com/docx-editor"><code>@​docx-editor</code></a>.dev/i18n@1.8.2</li>
</ul>
</li>
</ul>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li>See full diff in <a href="https://github.com/eigenpal/docx-editor/commits/HEAD/packages/react">compare view</a></li>
</ul>
</details>
<br />

Updates `@fortawesome/fontawesome-free` from 7.2.0 to 7.3.1
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/FortAwesome/Font-Awesome/releases">@​fortawesome/fontawesome-free's releases</a>.</em></p>
<blockquote>
<h2>Release 7.3.1</h2>
<p><strong>Change log available at <a href="https://fontawesome.com/docs/changelog/">https://fontawesome.com/docs/changelog/</a></strong></p>
<h2>Release 7.3.0</h2>
<p><strong>Change log available at <a href="https://fontawesome.com/docs/changelog/">https://fontawesome.com/docs/changelog/</a></strong></p>
</blockquote>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/FortAwesome/Font-Awesome/commit/14c65a3747d0f3b751f15831fc719236aea8729d"><code>14c65a3</code></a> Release 7.3.1 (<a href="https://redirect.github.com/FortAwesome/Font-Awesome/issues/21630">#21630</a>)</li>
<li><a href="https://github.com/FortAwesome/Font-Awesome/commit/70fb2dd154b617f62fc4ae5b0b7e2943bfd2aa96"><code>70fb2dd</code></a> Release 7.3.0 (<a href="https://redirect.github.com/FortAwesome/Font-Awesome/issues/21612">#21612</a>)</li>
<li>See full diff in <a href="https://github.com/FortAwesome/Font-Awesome/compare/7.2.0...7.3.1">compare view</a></li>
</ul>
</details>
<details>
<summary>Maintainer changes</summary>
<p>This version was pushed to npm by <a href="https://www.npmjs.com/~fortawesome-admin">fortawesome-admin</a>, a new releaser for <code>@​fortawesome/fontawesome-free</code> since your current version.</p>
</details>
<br />

Updates `@hocuspocus/provider` from 4.0.0 to 4.6.0
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/ueberdosis/hocuspocus/releases">@​hocuspocus/provider's releases</a>.</em></p>
<blockquote>
<h2>v4.6.0</h2>
<p>extension-redis will now slightly (setImmediate) delay forwarding messages to Redis, which improves performance a lot when many (500+) users are connected to the same document.</p>
<h2>What's Changed</h2>
<ul>
<li>feat/redis pending flushes by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1135">ueberdosis/hocuspocus#1135</a></li>
<li>fix: encode stateless message once when received operation via Redis … by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1136">ueberdosis/hocuspocus#1136</a></li>
</ul>
<p><strong>Full Changelog</strong>: <a href="https://github.com/ueberdosis/hocuspocus/compare/v4.5.0...v4.6.0">https://github.com/ueberdosis/hocuspocus/compare/v4.5.0...v4.6.0</a></p>
<h2>v4.5.0</h2>
<h2>What's Changed</h2>
<ul>
<li>feat: batch updates before sending to clients by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1130">ueberdosis/hocuspocus#1130</a></li>
<li>fix: ignore message in awarenessUpdateHandler if origin=this by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1129">ueberdosis/hocuspocus#1129</a></li>
<li>fix: when beforeHandleMessage throws, we don't want to process other messages that were already queued by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1123">ueberdosis/hocuspocus#1123</a></li>
</ul>
<p><strong>Full Changelog</strong>: <a href="https://github.com/ueberdosis/hocuspocus/compare/v4.4.0...v4.5.0">https://github.com/ueberdosis/hocuspocus/compare/v4.4.0...v4.5.0</a></p>
<h2>v4.4.0</h2>
<h2>What's Changed</h2>
<ul>
<li>feat: add <code>flushDelay</code> option for batching updates to reduce websocket traffic during heavy editing by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1118">ueberdosis/hocuspocus#1118</a></li>
<li>feat: add consistent state synchronization across Redis instances by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1119">ueberdosis/hocuspocus#1119</a></li>
<li>fix: make sure server.destroy() only runs once by <a href="https://github.com/DefV"><code>@​DefV</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1114">ueberdosis/hocuspocus#1114</a></li>
<li>fix: allow binding the server to a specific address by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1121">ueberdosis/hocuspocus#1121</a></li>
<li>build(deps): bump actions/checkout from 6 to 7 by <a href="https://github.com/dependabot"><code>@​dependabot</code></a>[bot] in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1117">ueberdosis/hocuspocus#1117</a></li>
<li>build(deps): bump hono from 4.12.21 to 4.12.25 by <a href="https://github.com/dependabot"><code>@​dependabot</code></a>[bot] in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1116">ueberdosis/hocuspocus#1116</a></li>
<li>build(deps): bump ws from 8.19.0 to 8.21.0 by <a href="https://github.com/dependabot"><code>@​dependabot</code></a>[bot] in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1115">ueberdosis/hocuspocus#1115</a></li>
</ul>
<h2>New Contributors</h2>
<ul>
<li><a href="https://github.com/DefV"><code>@​DefV</code></a> made their first contribution in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1114">ueberdosis/hocuspocus#1114</a></li>
</ul>
<p><strong>Full Changelog</strong>: <a href="https://github.com/ueberdosis/hocuspocus/compare/v4.3.0...v4.4.0">https://github.com/ueberdosis/hocuspocus/compare/v4.3.0...v4.4.0</a></p>
<h2>v4.3.0</h2>
<h2>What's Changed</h2>
<ul>
<li>feat: add <code>afterHandleMessage</code> hook to run after message handling completion by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1112">ueberdosis/hocuspocus#1112</a></li>
<li>feat: enforce pre-auth resource limits to safeguard server stability by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1113">ueberdosis/hocuspocus#1113</a></li>
</ul>
<p><strong>Full Changelog</strong>: <a href="https://github.com/ueberdosis/hocuspocus/compare/v4.2.0...v4.3.0">https://github.com/ueberdosis/hocuspocus/compare/v4.2.0...v4.3.0</a></p>
<h2>v4.2.0</h2>
<h2>What's Changed</h2>
<ul>
<li>feat: add <code>unloadImmediately</code> option to <code>disconnect()</code> for configurable document persistence behavior by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1111">ueberdosis/hocuspocus#1111</a></li>
</ul>
<p><strong>Full Changelog</strong>: <a href="https://github.com/ueberdosis/hocuspocus/compare/v4.1.2...v4.2.0">https://github.com/ueberdosis/hocuspocus/compare/v4.1.2...v4.2.0</a></p>
<h2>v4.1.2</h2>
<h2>What's Changed</h2>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Changelog</summary>
<p><em>Sourced from <a href="https://github.com/ueberdosis/hocuspocus/blob/main/CHANGELOG.md">@​hocuspocus/provider's changelog</a>.</em></p>
<blockquote>
<h1><a href="https://github.com/ueberdosis/hocuspocus/compare/v4.5.0...v4.6.0">4.6.0</a> (2026-08-10)</h1>
<h3>Bug Fixes</h3>
<ul>
<li>encode stateless message once when received operation via Redis ; this is a performance fix. (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1136">#1136</a>) (<a href="https://github.com/ueberdosis/hocuspocus/commit/b524b4b30299a64ffa1309f70a0fd6e761103d4a">b524b4b</a>)</li>
</ul>
<h1><a href="https://github.com/ueberdosis/hocuspocus/compare/v4.4.0...v4.5.0">4.5.0</a> (2026-08-04)</h1>
<h3>Bug Fixes</h3>
<ul>
<li>audit (<a href="https://github.com/ueberdosis/hocuspocus/commit/141360c256022deb5578c3902c3dfe0af8f6516e">141360c</a>)</li>
<li>flawky test relying on timings (<a href="https://github.com/ueberdosis/hocuspocus/commit/fe4a8e68801f1659624f53da745e595ad9f11c63">fe4a8e6</a>)</li>
<li>ignore message in awarenessUpdateHandler if origin=this (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1129">#1129</a>) (<a href="https://github.com/ueberdosis/hocuspocus/commit/08b25d4b258d932c68c999c14edcb4efc65c7a9b">08b25d4</a>)</li>
<li>update packages via audit --fix (<a href="https://github.com/ueberdosis/hocuspocus/commit/1dc9ca0ff35f1033136473d134cee8cb6b336281">1dc9ca0</a>)</li>
<li>when beforeHandleMessage throws, we don't want to process other messages that were already queued (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1123">#1123</a>) (<a href="https://github.com/ueberdosis/hocuspocus/commit/ed5dc40581cc829a6d0b04040717a8ee89296140">ed5dc40</a>)</li>
</ul>
<h3>Features</h3>
<ul>
<li>pnpm11 (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1133">#1133</a>) (<a href="https://github.com/ueberdosis/hocuspocus/commit/01c224ad9133340048c0e4f7bdce3981f4984d76">01c224a</a>)</li>
</ul>
<h1><a href="https://github.com/ueberdosis/hocuspocus/compare/v4.3.0...v4.4.0">4.4.0</a> (2026-07-13)</h1>
<h3>Bug Fixes</h3>
<ul>
<li>allow binding the server to a specific address (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1121">#1121</a>) (<a href="https://github.com/ueberdosis/hocuspocus/commit/408127b1c090356cc9148a801f314a8e6f863b09">408127b</a>)</li>
</ul>
<h3>Features</h3>
<ul>
<li>add <code>flushDelay</code> option for batching updates to reduce websocket traffic during heavy editing (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1118">#1118</a>) (<a href="https://github.com/ueberdosis/hocuspocus/commit/75594c05d57d48f2f70d4c9440c28b8226bf95ac">75594c0</a>)</li>
<li>add consistent state synchronization across Redis instances (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1119">#1119</a>) (<a href="https://github.com/ueberdosis/hocuspocus/commit/0051a6cb7618290d1f574da7ad61da2be77f839d">0051a6c</a>)</li>
</ul>
<h1><a href="https://github.com/ueberdosis/hocuspocus/compare/v4.2.0...v4.3.0">4.3.0</a> (2026-06-18)</h1>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/5c85b91af99544630200c438bfc5594a574d912e"><code>5c85b91</code></a> v4.6.0</li>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/d55367e6d3c20167d1daf920aa1e1094909a58ba"><code>d55367e</code></a> Feat/redis pending flushes (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1135">#1135</a>)</li>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/b524b4b30299a64ffa1309f70a0fd6e761103d4a"><code>b524b4b</code></a> fix: encode stateless message once when received operation via Redis ; this i...</li>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/3ec608445b8e024e15759504cca9ff1f7b09edf8"><code>3ec6084</code></a> build(deps): bump pnpm/action-setup from 5 to 6.0.9 (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1131">#1131</a>)</li>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/7827bded7c9181513a3b7c94acbaee0e4059d066"><code>7827bde</code></a> v4.5.0</li>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/141360c256022deb5578c3902c3dfe0af8f6516e"><code>141360c</code></a> fix: audit</li>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/1dc9ca0ff35f1033136473d134cee8cb6b336281"><code>1dc9ca0</code></a> fix: update packages via audit --fix</li>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/01c224ad9133340048c0e4f7bdce3981f4984d76"><code>01c224a</code></a> feat: pnpm11 (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1133">#1133</a>)</li>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/d9f87a6b738afa718dc0dd47580e02eacc764ce8"><code>d9f87a6</code></a> Feat/batch updates before sending to clients (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1130">#1130</a>)</li>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/a5812e6ec2fbeeefd6dd388a39e1d16fd192f6db"><code>a5812e6</code></a> chore: sync default port with playground</li>
<li>Additional commits viewable in <a href="https://github.com/ueberdosis/hocuspocus/compare/v4.0.0...v4.6.0">compare view</a></li>
</ul>
</details>
<br />

Updates `@mantine/charts` from 9.1.1 to 9.5.2
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/mantinedev/mantine/releases">@​mantine/charts's releases</a>.</em></p>
<blockquote>
<h2>9.5.2</h2>
<ul>
<li><code>[@mantine/hooks]</code> use-debounced-value: Fix <code>leading: true</code> firing multiple times per burst and emiting a stale value (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9119">#9119</a>)</li>
<li><code>[@mantine/schedule]</code> Fix recurring events not working with timzones (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9112">#9112</a>)</li>
<li><code>[@mantine/dates]</code> Fix <code>minDate</code> used for default date in some cases (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9117">#9117</a>)</li>
<li><code>[@mantine/core]</code> Tooltip: Fix tooltip setting NaN in top/left position style when event position values cannot be read (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9131">#9131</a>)</li>
<li><code>[@mantine/dates]</code> TimePicker: Fix incorrect focus handling of partially filled hours field (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9128">#9128</a>)</li>
<li><code>[@mantine/core]</code> RollingNumber: Fix incorrect copy event handling (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9132">#9132</a>)</li>
<li><code>[@mantine/core]</code> Notification: Fix incorrect <code>closeButtonProps</code> type (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9134">#9134</a>)</li>
<li><code>[@mantine/code-highlight]</code> Add support for lazy languages loading (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9141">#9141</a>)</li>
<li><code>[@mantine/code-highlight]</code> CodeHighlight: Add prop to keep indentation of the first line of the code block (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9140">#9140</a>)</li>
<li><code>[@mantine/dates]</code> Add missing formatting functions to MiniCalendarm DateInput and YarsList components</li>
<li><code>[@mantine/schedule]</code> WeekView: Improve performance of events positioning algorithm (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9075">#9075</a>)</li>
<li><code>[@mantine/form]</code> Add new useWatchValue hook</li>
<li><code>[@mantine/core]</code> Fix Combobox-based components not working correctly with Chrome autocomplete</li>
</ul>
<h2>9.5.1</h2>
<ul>
<li><code>[@mantine/tiptap]</code> Fix controls being initially disabledbefore element is focused</li>
<li><code>[@mantine/tiptap]</code> Fix source code control wrapping content with extra p tag</li>
<li><code>[@mantine/hooks]</code> use-scroll-spy: Allow usage with refs (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9025">#9025</a>)</li>
<li><code>[@mantine/core]</code> ColorInput: Add support for fullWidth prop (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9061">#9061</a>)</li>
<li><code>[@mantine/core]</code> Checkbox: Fix incottect indeterminate aria attributes handling in Checkbox.Card (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9095">#9095</a>)</li>
<li><code>[@mantine/core]</code> FloatingIndicator: Fix position and size calculation under scaled ancestors (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9071">#9071</a>)</li>
<li><code>[@mantine/core]</code> Tooltip: Add interactive prop support (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9072">#9072</a>)</li>
<li><code>[@mantine/core]</code> Cascader: Add safe area polygon support</li>
<li><code>[@mantine/core]</code> PasswordInput: Add option to change whether the visibility toggle is focusable (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9090">#9090</a>)</li>
<li><code>[@mantine/charts]</code> ScatterChart: Add option to add second y axis</li>
<li><code>[@mantine/schedule]</code> YearView: Add <code>renderDay</code> prop support</li>
<li><code>[@mantine/schedule]</code> YearView: Add option to hide weekend days</li>
<li><code>[@mantine/core]</code> InputWrapper: Fix <code>component: div</code> triggering typescript error if passed to <code>descriptionProps</code></li>
<li><code>[@mantine/schedule]</code> ResourcesMonthView: Add option to resize events</li>
<li><code>[@mantine/core]</code> FloatingWindow: Add support for  <code>onSizeChange</code> and <code>onResizeStart</code> props (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9085">#9085</a>)</li>
</ul>
<h2>9.5.0 🤖</h2>
<p><a href="https://mantine.dev/changelog/9-5-0">View changelog with demos on mantine.dev website</a></p>
<h2>Support Mantine development</h2>
<p>You can now sponsor Mantine development with <a href="https://opencollective.com/mantinedev">OpenCollective</a>.
All funds are used to improve Mantine and create new features and components.</p>
<h2>Migration to oxc</h2>
<p>Mantine has migrated its linting and formatting toolchain from ESLint and Prettier
to <a href="https://oxc.rs">oxc</a> – <a href="https://www.npmjs.com/package/oxlint">oxlint</a> is now used
as the linter and <a href="https://www.npmjs.com/package/oxfmt">oxfmt</a> as the formatter. Both
tools are written in Rust and are significantly faster than their predecessors, which
makes linting and formatting the entire codebase almost instant.</p>
<p>The shared configuration is available as a new
<a href="https://mantine.dev/oxc-config-mantine">oxc-config-mantine</a> package (a replacement for the previous</p>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/mantinedev/mantine/commit/8a284e2c2c53a9cb6f39f5dc389bf41b7a2073f8"><code>8a284e2</code></a> [release] Version: 9.5.2</li>
<li><a href="https://github.com/mantinedev/mantine/commit/0f57eaf5ae90c9e870fbb2a4cdd61a1d58c4c01d"><code>0f57eaf</code></a> [release] Version: 9.5.1</li>
<li><a href="https://github.com/mantinedev/mantine/commit/1e120595fdde5a414616df908bb3e600021d092e"><code>1e12059</code></a> [<code>@​mantine/charts</code>] ScatterChart: Add option to add second y axis</li>
<li><a href="https://github.com/mantinedev/mantine/commit/ca9bc6f156b63f1a10918d94ec31ec18e4e60546"><code>ca9bc6f</code></a> [release] Version: 9.5.1-alpha.1</li>
<li><a href="https://github.com/mantinedev/mantine/commit/8f1ad1bbe545c9cafafc5aef5b059d3d48e676a6"><code>8f1ad1b</code></a> [release] Version: 9.5.1-alpha.0</li>
<li><a href="https://github.com/mantinedev/mantine/commit/f1d330613f54dc9319d176e6d8ba5ebff233da18"><code>f1d3306</code></a> [release] Version: 9.5.0</li>
<li><a href="https://github.com/mantinedev/mantine/commit/732056219a0283f5822001981d7f652e632c4c87"><code>7320562</code></a> [release] Version: 9.4.3</li>
<li><a href="https://github.com/mantinedev/mantine/commit/170c45a5feed2386a464a7f05ae3daf6379cea04"><code>170c45a</code></a> Merge branch '9.5'</li>
<li><a href="https://github.com/mantinedev/mantine/commit/de21a8203060ba29441ab7623244339748e4319d"><code>de21a82</code></a> [release] Version: 9.4.3-alpha.0</li>
<li><a href="https://github.com/mantinedev/mantine/commit/e5752de4067bd58f6cdd970660b3c8469a56d4e5"><code>e5752de</code></a> [release] Version: 9.4.2</li>
<li>Additional commits viewable in <a href="https://github.com/mantinedev/mantine/commits/9.5.2/packages/@mantine/charts">compare view</a></li>
</ul>
</details>
<details>
<summary>Maintainer changes</summary>
<p>This version was pushed to npm by <a href="https://www.npmjs.com/~GitHub%20Actions">GitHub Actions</a>, a new releaser for <code>@​mantine/charts</code> since your current version.</p>
</details>
<br />

Updates `@mantine/colors-generator` from 9.1.1 to 9.5.2
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/mantinedev/mantine/releases">@​mantine/colors-generator's releases</a>.</em></p>
<blockquote>
<h2>9.5.2</h2>
<ul>
<li><code>[@mantine/hooks]</code> use-debounced-value: Fix <code>leading: true</code> firing multiple times per burst and emiting a stale value (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9119">#9119</a>)</li>
<li><code>[@mantine/schedule]</code> Fix recurring events not working with timzones (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9112">#9112</a>)</li>
<li><code>[@mantine/dates]</code> Fix <code>minDate</code> used for default date in some cases (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9117">#9117</a>)</li>
<li><code>[@mantine/core]</code> Tooltip: Fix tooltip setting NaN in top/left position style when event position values cannot be read (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9131">#9131</a>)</li>
<li><code>[@mantine/dates]</code> TimePicker: Fix incorrect focus handling of partially filled hours field (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9128">#9128</a>)</li>
<li><code>[@mantine/core]</code> RollingNumber: Fix incorrect copy event handling (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9132">#9132</a>)</li>
<li><code>[@mantine/core]</code> Notification: Fix incorrect <code>closeButtonProps</code> type (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9134">#9134</a>)</li>
<li><code>[@mantine/code-highlight]</code> Add support for lazy languages loading (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9141">#9141</a>)</li>
<li><code>[@mantine/code-highlight]</code> CodeHighlight: Add prop to keep indentation of the first line of the code block (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9140">#9140</a>)</li>
<li><code>[@mantine/dates]</code> Add missing formatting functions to MiniCalendarm DateInput and YarsList components</li>
<li><code>[@mantine/schedule]</code> WeekView: Improve performance of events positioning algorithm (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9075">#9075</a>)</li>
<li><code>[@mantine/form]</code> Add new useWatchValue hook</li>
<li><code>[@mantine/core]</code> Fix Combobox-based components not working correctly with Chrome autocomplete</li>
</ul>
<h2>9.5.1</h2>
<ul>
<li><code>[@mantine/tiptap]</code> Fix controls being initially disabledbefore element is focused</li>
<li><code>[@mantine/tiptap]</code> Fix source code control wrapping content with extra p tag</li>
<li><code>[@mantine/hooks]</code> use-scroll-spy: Allow usage with refs (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9025">#9025</a>)</li>
<li><code>[@mantine/core]</code> ColorInput: Add support for fullWidth prop (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9061">#9061</a>)</li>
<li><code>[@mantine/core]</code> Checkbox: Fix incottect indeterminate aria attributes handling in Checkbox.Card (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9095">#9095</a>)</li>
<li><code>[@mantine/core]</code> FloatingIndicator: Fix position and size calculation under scaled ancestors (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9071">#9071</a>)</li>
<li><code>[@mantine/core]</code> Tooltip: Add interactive prop support (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9072">#9072</a>)</li>
<li><code>[@mantine/core]</code> Cascader: Add safe area polygon support</li>
<li><code>[@mantine/core]</code> PasswordInput: Add option to change whether the visibility toggle is focusable (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9090">#9090</a>)</li>
<li><code>[@mantine/charts]</code> ScatterChart: Add option...

_Description has been truncated_

<details><summary>Comment — nathanpond, 2026-08-31</summary>

**Held — same three blockers as archived-107/archived-131** (this is the same group regrouped): `@blocknote/*` 0.54 (removes `YjsThreadStore`/`User` from `@blocknote/core/comments` → 15 `tsc` errors), `@eigenpal/docx-editor-*` 1.9.0 (deprecated on npm at every version; drops the transitive `y-prosemirror` the SPA imports), and `@hocuspocus/provider` 4.6 (must land with the server in archived-104). To let the other ~30 updates through, exclude those packages from the `spa-minor-patch` group in `.github/dependabot.yml` — happy to do that on request.

</details>

<details><summary>Comment — nathanpond, 2026-08-31</summary>

@dependabot recreate

</details>

<details><summary>Comment — dependabot[bot], 2026-08-31</summary>

Looks like these dependencies are updatable in another way, so this is no longer needed.

</details>

---

## archived-140 — Standardise Node.js on 24 (Active LTS); fix the executor image

`MERGED (merged 2026-08-31)` · nathanpond · opened 2026-08-31 · `feat/139-node-24` → `master`

Closes archived-139
Closes archived-39

## What
Pins Node 24 everywhere (`.nvmrc`, `engines.node`, both sidecar Dockerfiles), and in doing so fixes the executor image, which has never actually run.

## Findings along the way
- **isolated-vm 5 cannot build on Node 24** — its C++17 build hits `#error "C++20 or later required."` in Node 24's V8 headers. 7.0.1 (Dependabot archived-102) is the supported version; taken here, so **archived-102 is superseded**.
- **The executor image was already broken on `node:22`** — `npm install --ignore-scripts` skips isolated-vm's node-gyp build, so `master`'s image crashes on boot with `Cannot find module './out/isolated_vm'` (control build reproduced it). Nobody had run it (archived-114).
- **npm ≥ 11.19 silently no-ops unapproved install scripts** — `npm rebuild isolated-vm` reported "rebuilt successfully" in 1.6 s without building. Fixed with `"allowScripts": { "isolated-vm": true }` in `package.json` and `--foreground-scripts`; the Dockerfile now verifies by `require`-ing the addon.
- `services/executor` had no lockfile (archived-39) — added.

## Verification
- `docker build services/hocuspocus` → boots on Node 24.20.0, `Hocuspocus listening on port 1234`.
- `docker build services/executor` → boots, `Connected to NATS`, and executes a JS transformer (isolated-vm 7.0.1) and a Python transformer (Pyodide) end-to-end through `pipeline-code-run.>` request/reply — both return the expected rows.
- SPA / hocuspocus lockfiles untouched; `engines` only adds a version declaration.

## Follow-ups
- archived-105 / archived-101 (`@types/node` 26): closed — types track the runtime major; they return as a 24→26 bump when Node 26 goes LTS (October 2026).
- archived-114: add the executor to `docker-compose.yml` (now that its image works).
- Separate finding filed from the smoke test: a plain NATS `request()` on `pipeline-code-run.>` receives the JetStream publish ack before the executor's reply.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Y5ie3qTEptr4MjYw5i6a5F

---

## archived-142 — Bump node from 24-alpine to 26-alpine in /services/hocuspocus

`CLOSED` · app/dependabot · opened 2026-08-31 · `dependabot/docker/services/hocuspocus/node-26-alpine` → `master`

> [!WARNING]
> Cooldown could not be applied because no publication date was available from the registry.
>

Bumps node from 24-alpine to 26-alpine.


[![Dependabot compatibility score](https://dependabot-badges.githubapp.com/badges/compatibility_score?dependency-name=node&package-manager=docker&previous-version=24-alpine&new-version=26-alpine)](https://docs.github.com/en/github/managing-security-vulnerabilities/about-dependabot-security-updates#about-compatibility-scores)

Dependabot will resolve any conflicts with this PR as long as you don't alter it yourself. You can also trigger a rebase manually by commenting `@dependabot rebase`.

[//]: # (dependabot-automerge-start)
[//]: # (dependabot-automerge-end)

---

<details>
<summary>Dependabot commands and options</summary>
<br />

You can trigger Dependabot actions by commenting on this PR:
- `@dependabot rebase` will rebase this PR
- `@dependabot recreate` will recreate this PR, overwriting any edits that have been made to it
- `@dependabot show <dependency name> ignore conditions` will show all of the ignore conditions of the specified dependency
- `@dependabot ignore this major version` will close this PR and stop Dependabot creating any more for this major version (unless you reopen the PR or upgrade to it yourself)
- `@dependabot ignore this minor version` will close this PR and stop Dependabot creating any more for this minor version (unless you reopen the PR or upgrade to it yourself)
- `@dependabot ignore this dependency` will close this PR and stop Dependabot creating any more for this dependency (unless you reopen the PR or upgrade to it yourself)


</details>

<details><summary>Comment — nathanpond, 2026-08-31</summary>

Closing: the runtime was standardised on **Node 24 (Active LTS)** in archived-140 (`.nvmrc`, `engines`, both images). Node 26 is Current until it enters LTS in October 2026; we'll take 24 → 26 deliberately then. `dependabot.yml` now ignores major bumps of the `node` base image so this doesn't reopen weekly.

</details>

<details><summary>Comment — dependabot[bot], 2026-08-31</summary>

OK, I won't notify you again about this release, but will get in touch when a new version is available. If you'd rather skip all updates until the next major or minor version, let me know by commenting `@dependabot ignore this major version` or `@dependabot ignore this minor version`. You can also ignore all major, minor, or patch releases for a dependency by adding an [`ignore` condition](https://docs.github.com/en/code-security/supply-chain-security/configuration-options-for-dependency-updates#ignore) with the desired `update_types` to your config file.

If you change your mind, just re-open this PR and I'll resolve any conflicts on it.

</details>

---

## archived-143 — Bump node from 24-alpine to 26-alpine in /services/executor

`CLOSED` · app/dependabot · opened 2026-08-31 · `dependabot/docker/services/executor/node-26-alpine` → `master`

> [!WARNING]
> Cooldown could not be applied because no publication date was available from the registry.
>

Bumps node from 24-alpine to 26-alpine.


[![Dependabot compatibility score](https://dependabot-badges.githubapp.com/badges/compatibility_score?dependency-name=node&package-manager=docker&previous-version=24-alpine&new-version=26-alpine)](https://docs.github.com/en/github/managing-security-vulnerabilities/about-dependabot-security-updates#about-compatibility-scores)

Dependabot will resolve any conflicts with this PR as long as you don't alter it yourself. You can also trigger a rebase manually by commenting `@dependabot rebase`.

[//]: # (dependabot-automerge-start)
[//]: # (dependabot-automerge-end)

---

<details>
<summary>Dependabot commands and options</summary>
<br />

You can trigger Dependabot actions by commenting on this PR:
- `@dependabot rebase` will rebase this PR
- `@dependabot recreate` will recreate this PR, overwriting any edits that have been made to it
- `@dependabot show <dependency name> ignore conditions` will show all of the ignore conditions of the specified dependency
- `@dependabot ignore this major version` will close this PR and stop Dependabot creating any more for this major version (unless you reopen the PR or upgrade to it yourself)
- `@dependabot ignore this minor version` will close this PR and stop Dependabot creating any more for this minor version (unless you reopen the PR or upgrade to it yourself)
- `@dependabot ignore this dependency` will close this PR and stop Dependabot creating any more for this dependency (unless you reopen the PR or upgrade to it yourself)


</details>

<details><summary>Comment — nathanpond, 2026-08-31</summary>

Closing: the runtime was standardised on **Node 24 (Active LTS)** in archived-140 (`.nvmrc`, `engines`, both images). Node 26 is Current until it enters LTS in October 2026; we'll take 24 → 26 deliberately then. `dependabot.yml` now ignores major bumps of the `node` base image so this doesn't reopen weekly.

</details>

<details><summary>Comment — dependabot[bot], 2026-08-31</summary>

OK, I won't notify you again about this release, but will get in touch when a new version is available. If you'd rather skip all updates until the next major or minor version, let me know by commenting `@dependabot ignore this major version` or `@dependabot ignore this minor version`. You can also ignore all major, minor, or patch releases for a dependency by adding an [`ignore` condition](https://docs.github.com/en/code-security/supply-chain-security/configuration-options-for-dependency-updates#ignore) with the desired `update_types` to your config file.

If you change your mind, just re-open this PR and I'll resolve any conflicts on it.

</details>

---

## archived-144 — Bump bpmn-js from 18.25.1 to 18.26.0

`MERGED (merged 2026-08-31)` · app/dependabot · opened 2026-08-31 · `dependabot/npm_and_yarn/bpmn-js-18.26.0` → `master`

Bumps [bpmn-js](https://github.com/bpmn-io/bpmn-js) from 18.25.1 to 18.26.0.
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/bpmn-io/bpmn-js/releases">bpmn-js's releases</a>.</em></p>
<blockquote>
<h2>v18.26.0</h2>
<h3>Changes</h3>
<ul>
<li>chore(CHANGELOG): update to v18.26.0  d85caf06</li>
<li>deps: update to bpmn-font@0.13.0  a0fc447f</li>
<li>deps: update to bpmn-moddle@10.2.0  e8915d7a</li>
<li>deps: update to diagram-js@15.25.0  e9c84adb</li>
<li>feat: add themeable, WCAG AA compliant primary accent  a56516e6</li>
<li>ci: verify pull request quality  91437959</li>
<li>ci: verify pull request quality  34d6362b</li>
</ul>
<hr />
<p><a href="https://github.com/bpmn-io/bpmn-js/compare/v18.25.1...v18.26.0">https://github.com/bpmn-io/bpmn-js/compare/v18.25.1...v18.26.0</a></p>
</blockquote>
</details>
<details>
<summary>Changelog</summary>
<p><em>Sourced from <a href="https://github.com/bpmn-io/bpmn-js/blob/develop/CHANGELOG.md">bpmn-js's changelog</a>.</em></p>
<blockquote>
<h2>18.26.0</h2>
<ul>
<li><code>FEAT</code>: give resize handle a border radius (<a href="https://redirect.github.com/bpmn-io/diagram-js/pull/1100">bpmn-io/diagram-js#1100</a>)</li>
<li><code>FEAT</code>: give segment dragger a border radius (<a href="https://redirect.github.com/bpmn-io/diagram-js/pull/1100">bpmn-io/diagram-js#1100</a>)</li>
<li><code>FEAT</code>: add <code>--accent-color</code> theming token (<a href="https://redirect.github.com/bpmn-io/bpmn-js/pull/2492">#2492</a>)</li>
<li><code>FIX</code>: use WCAG AA compliant primary accent color (<a href="https://redirect.github.com/bpmn-io/bpmn-js/pull/2492">#2492</a>)</li>
<li><code>DEPS</code>: update to <code>diagram-js@15.25.0</code></li>
<li><code>DEPS</code>: update to <code>bpmn-moddle@10.2.0</code></li>
<li><code>DEPS</code>: update to <code>bpmn-font@0.13.0</code></li>
</ul>
</blockquote>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/bpmn-io/bpmn-js/commit/f3a57cd8ee83fb05adee836bc118c6819c219049"><code>f3a57cd</code></a> 18.26.0</li>
<li><a href="https://github.com/bpmn-io/bpmn-js/commit/d85caf06c1950a39cae13544a4955916131b4327"><code>d85caf0</code></a> chore(CHANGELOG): update to v18.26.0</li>
<li><a href="https://github.com/bpmn-io/bpmn-js/commit/a0fc447f8fa9b35a330fcb102f27fe73a8342ab2"><code>a0fc447</code></a> deps: update to bpmn-font@0.13.0</li>
<li><a href="https://github.com/bpmn-io/bpmn-js/commit/e8915d7af33202547fa5214372f662f7f6b7bcdc"><code>e8915d7</code></a> deps: update to bpmn-moddle@10.2.0</li>
<li><a href="https://github.com/bpmn-io/bpmn-js/commit/e9c84adba50e535fd9f6c9d97917241d35cee4ae"><code>e9c84ad</code></a> deps: update to diagram-js@15.25.0</li>
<li><a href="https://github.com/bpmn-io/bpmn-js/commit/a56516e6fc86992ea55af844853cb9c0c6a8b6c8"><code>a56516e</code></a> feat: add themeable, WCAG AA compliant primary accent</li>
<li><a href="https://github.com/bpmn-io/bpmn-js/commit/9143795913128ed32b6f4c0996ee8bd6b3a4f6d5"><code>9143795</code></a> ci: verify pull request quality</li>
<li><a href="https://github.com/bpmn-io/bpmn-js/commit/34d6362bafd9de621e4bfb96f746191f8a31332a"><code>34d6362</code></a> ci: verify pull request quality</li>
<li>See full diff in <a href="https://github.com/bpmn-io/bpmn-js/compare/v18.25.1...v18.26.0">compare view</a></li>
</ul>
</details>
<br />


[![Dependabot compatibility score](https://dependabot-badges.githubapp.com/badges/compatibility_score?dependency-name=bpmn-js&package-manager=npm_and_yarn&previous-version=18.25.1&new-version=18.26.0)](https://docs.github.com/en/github/managing-security-vulnerabilities/about-dependabot-security-updates#about-compatibility-scores)

Dependabot will resolve any conflicts with this PR as long as you don't alter it yourself. You can also trigger a rebase manually by commenting `@dependabot rebase`.

[//]: # (dependabot-automerge-start)
[//]: # (dependabot-automerge-end)

---

<details>
<summary>Dependabot commands and options</summary>
<br />

You can trigger Dependabot actions by commenting on this PR:
- `@dependabot rebase` will rebase this PR
- `@dependabot recreate` will recreate this PR, overwriting any edits that have been made to it
- `@dependabot show <dependency name> ignore conditions` will show all of the ignore conditions of the specified dependency
- `@dependabot ignore this major version` will close this PR and stop Dependabot creating any more for this major version (unless you reopen the PR or upgrade to it yourself)
- `@dependabot ignore this minor version` will close this PR and stop Dependabot creating any more for this minor version (unless you reopen the PR or upgrade to it yourself)
- `@dependabot ignore this dependency` will close this PR and stop Dependabot creating any more for this dependency (unless you reopen the PR or upgrade to it yourself)


</details>

---

## archived-145 — Run the executor sidecar as part of the local stack

`MERGED (merged 2026-08-31)` · nathanpond · opened 2026-08-31 · `feat/114-executor-in-stack` → `master`

Closes archived-114

## What
Adds `services/executor` to `infra/docker-compose.yml` and `infra/ensure-up.sh` so pipeline code nodes have a consumer in the documented dev stack, with a real health signal.

- **Compose service** `executor`: built from `../services/executor`, `NATS_URL=nats://nats:4222`, depends on `nats` (healthy) and `nats-init` (completed), `restart: unless-stopped`, no ports.
- **Health probe over NATS**: the sidecar answers `executor.health` and ships `dist/healthcheck.js`; the compose `healthcheck` requests it (3 s timeout). The subject sits outside `pipeline-code-run.>`, which a JetStream stream captures (archived-141).
- **`ensure-up.sh`**: `executor` in `REQUIRED_SERVICES`; readiness = container health; build-input stamp (`infra/mounts/executor/.build-input-hash`) so editing `src/` rebuilds the image, mirroring hocuspocus.
- Header comment in `index.ts` corrected — it is a core queue subscriber, not a durable consumer (part of archived-49).
- README local-stack list + `docs/codebase/Integrations.md`.

## Verification
- [x] `./infra/ensure-up.sh` → rebuilds hocuspocus (Node 24 inputs) and executor, starts the stack, `autonate-executor … (healthy)`.
- [x] `docker exec autonate-executor node dist/healthcheck.js` → exit 0.
- [x] JS transformer (isolated-vm 7) and Python transformer (Pyodide) sent on `pipeline-code-run.>` are answered by the compose-managed container with the expected rows.
- [x] Second `./infra/ensure-up.sh` → "already running and ready" in 1 s.
- [x] `restart=unless-stopped`, zero published ports.

Note: with a working executor attached, archived-141 (the runner reading JetStream's ack as the reply) becomes the next thing code nodes hit.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Y5ie3qTEptr4MjYw5i6a5F

---

## archived-146 — Executor replies: explicit inbox, no JetStream stream on pipeline-code-run.>

`MERGED (merged 2026-08-31)` · nathanpond · opened 2026-08-31 · `fix/141-jetstream-ack` → `master`

Closes archived-141
Closes archived-49

## What
`JetStreamCodeNodeRunner` used a plain `RequestAsync` on `pipeline-code-run.<run>.<node>` while `NatsStreamProvisioner` captured that subject in a `pipeline-code-runs` stream. JetStream therefore answered every request with a PubAck (`{"stream":…,"seq":…}`) before the executor could, and the runner parsed the ack as a failed `CodeNodeReply` ("Executor sidecar reported an unknown failure") and discarded the real reply. Masked until archived-139/archived-114 made the executor image actually run.

## Changes
- **`NatsStreamProvisioner`** — `pipeline-code-runs` removed from `DesiredStreams`, added to `LegacyStreamsToRemove` so already-provisioned servers are cleaned on the next boot. Request/reply is core NATS; the stream bought nothing (the sidecar is a core queue subscriber).
- **`JetStreamCodeNodeRunner`** — `NewInbox` + `SubscribeCoreAsync` + `PublishAsync(replyTo:)`; reads the inbox until a message shaped like a `CodeNodeReply` (has `success`) arrives, ignoring anything else. Same 30 s timeout/cancellation semantics. Stale header comment fixed.
- **`JetStreamCodeNodeRunnerTests`** (new, real NATS from the test infra) — provisions a throw-away stream over the subject, runs a fake executor that answers *after* the ack, asserts the runner returns the executor's reply; plus theory cases for the ack/reply classifier.
- `docs/codebase/{Integrations,Architecture,Structure}.md` — no more "durable consumer" / stream description (the three stale-comment sites from archived-49 are now all corrected: runner header, provisioner block, `services/executor/src/index.ts` in archived-145).

## Verification
- [x] `JetStreamCodeNodeRunnerTests` → 6/6.
- [x] Same integration test against the **old** runner (stashed fix) → fails with `Executor sidecar reported an unknown failure`.
- [x] Live boot (`dotnet run --no-launch-profile`) → `Removed legacy JetStream stream 'pipeline-code-runs'`; `nats stream ls` afterwards: `workflow-execution` only.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Y5ie3qTEptr4MjYw5i6a5F

---

## archived-148 — E2E: update the four stale DataStoresAdminTests specs to the current UI

`MERGED (merged 2026-08-31)` · nathanpond · opened 2026-08-31 · `fix/147-datastores-e2e` → `master`

Closes archived-147

## What
Rewrites the four `DataStoresAdminTests` specs that had drifted from the product (file-store detail → SVAR file manager on 2026-06-06; file-backed datasets → mandatory file pick). Test-only change; no product code touched.

- **Upload**: `Upload to current folder` → Modal `Upload to /` → Dropzone's hidden `input[type=file]` via `SetInputFiles` → `Upload N`. Asserts the file *stem* (SVAR renders name/extension as separate spans).
- **New folder**: `Add New` → `Add new folder` (portal-rendered menu of plain divs) → SVAR `.wx-modal` prompt (no dialog role) → `OK`. Asserts the folder in the tree.
- **Dataset create / edit over a file store**: seeds one CSV with `POST /api/datastores/{id}/files` (`page.APIRequest` multipart — the same call the SPA makes), picks it under *File*, then Create/Save. Edit spec asserts the renamed row is still `datastore`-backed (server update is patch-style, so the file scope survives).
- Three shared helpers replace the copy-pasted store-creation blocks.

## Notes from the rewrite
- The old Edit spec was failing at the *create* step, not the edit — the edit form only exposes name/description/cron and leaves the file scope alone.
- SVAR's toolbar/menu/prompt have no stable roles; targeted by exact text and `.wx-modal`. If that proves brittle, `data-testid`s on `DataStoreFileManager.tsx`'s toolbar are the next step.

## Verification
- [x] `dotnet test tests/AutoNate.E2E.Tests --filter DataStoresAdminTests` → **16/16** (was 12/16 on `master`).
- [x] Each rewritten spec reproduced by hand in a browser first (flows, labels, network calls) before encoding.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Y5ie3qTEptr4MjYw5i6a5F

---

## archived-149 — Bump ws from 8.20.1 to 8.21.3 in /services/hocuspocus

`MERGED (merged 2026-08-31)` · app/dependabot · opened 2026-08-31 · `dependabot/npm_and_yarn/services/hocuspocus/ws-8.21.3` → `master`

Bumps [ws](https://github.com/websockets/ws) from 8.20.1 to 8.21.3.
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/websockets/ws/releases">ws's releases</a>.</em></p>
<blockquote>
<h2>8.21.3</h2>
<h1>Bug fixes</h1>
<ul>
<li>The server now correctly rejects permessage-deflate offers if the incoming
<code>client_max_window_bits</code> parameter value is smaller than its configured
<code>clientMaxWindowBits</code> (e97a20ea).</li>
</ul>
<h2>8.21.2</h2>
<h1>Bug fixes</h1>
<ul>
<li>Fixed a test for <a href="https://github.com/nodejs/citgm">CITGM</a> (2eb3be0b).</li>
</ul>
<h2>8.21.1</h2>
<h1>Bug fixes</h1>
<ul>
<li>Empty fragments are now counted toward the limit (a2f4e7c0).</li>
<li>The default values of the <code>maxBufferedChunks</code> and <code>maxFragments</code> options have
been reduced (f197ac65).</li>
</ul>
<h2>8.21.0</h2>
<h1>Features</h1>
<ul>
<li>Introduced the <code>maxBufferedChunks</code> and <code>maxFragments</code> options (2b2abd45).</li>
</ul>
<h1>Bug fixes</h1>
<ul>
<li>Fixed a remote memory exhaustion DoS vulnerability (2b2abd45).</li>
</ul>
<p>A high volume of tiny fragments and data chunks could be sent by a peer, using
modest network traffic, to crash a <code>ws</code> server or client due to OOM.</p>
<pre lang="js"><code>import { WebSocket, WebSocketServer } from 'ws';
<p>const wss = new WebSocketServer({ port: 0 }, function () {
const data = Buffer.alloc(1);
const options = { fin: false };
const { port } = wss.address();
const ws = new WebSocket(<code>ws://localhost:${port}</code>);</p>
<p>ws.on('open', function () {
(function send() {
ws.send(data, options, function (err) {
if (err) return;
send();
});
})();
});
&lt;/tr&gt;&lt;/table&gt;
</code></pre></p>
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/websockets/ws/commit/c791e707eab3c13dd9a261d2479c3cc4a49a6fed"><code>c791e70</code></a> [dist] 8.21.3</li>
<li><a href="https://github.com/websockets/ws/commit/e97a20eaa6f2ad7969419eed732a506453251eb9"><code>e97a20e</code></a> [fix] Reject offers with <code>client_max_window_bits</code> below config</li>
<li><a href="https://github.com/websockets/ws/commit/787ebf22ce3d091fb6f931d20b4c7e914ba7cf85"><code>787ebf2</code></a> [dist] 8.21.2</li>
<li><a href="https://github.com/websockets/ws/commit/b4d62ebad40c3b925c84ff305a47975406015422"><code>b4d62eb</code></a> Revert &quot;[ci] Trust Coveralls Homebrew tap&quot;</li>
<li><a href="https://github.com/websockets/ws/commit/e4bb883723a0c18452eea10a74139901ae33c61d"><code>e4bb883</code></a> [security] Use GitHub PVR as main reporting channel</li>
<li><a href="https://github.com/websockets/ws/commit/2eb3be0bff2453e2654b1315c5872e8d5d424a50"><code>2eb3be0</code></a> [test] Skip test on Node.js versions where it does not apply</li>
<li><a href="https://github.com/websockets/ws/commit/ae1de54330cef77e487548890fabfeb9aae1d83d"><code>ae1de54</code></a> [dist] 8.21.1</li>
<li><a href="https://github.com/websockets/ws/commit/8e9511b86b3fc6deebbd97dd9af7c9056deea8d1"><code>8e9511b</code></a> [ci] Trust Coveralls Homebrew tap</li>
<li><a href="https://github.com/websockets/ws/commit/f197ac65140920bdcecdab74bfc69c2d7858e55d"><code>f197ac6</code></a> [fix] Lower default values of <code>maxBufferedChunks</code> and <code>maxFragments</code></li>
<li><a href="https://github.com/websockets/ws/commit/8df8265c2f63fd44af3193a98e23cf38888cd991"><code>8df8265</code></a> [ci] Update actions/checkout action to v7</li>
<li>Additional commits viewable in <a href="https://github.com/websockets/ws/compare/8.20.1...8.21.3">compare view</a></li>
</ul>
</details>
<br />


[![Dependabot compatibility score](https://dependabot-badges.githubapp.com/badges/compatibility_score?dependency-name=ws&package-manager=npm_and_yarn&previous-version=8.20.1&new-version=8.21.3)](https://docs.github.com/en/github/managing-security-vulnerabilities/about-dependabot-security-updates#about-compatibility-scores)

Dependabot will resolve any conflicts with this PR as long as you don't alter it yourself. You can also trigger a rebase manually by commenting `@dependabot rebase`.

[//]: # (dependabot-automerge-start)
[//]: # (dependabot-automerge-end)

---

<details>
<summary>Dependabot commands and options</summary>
<br />

You can trigger Dependabot actions by commenting on this PR:
- `@dependabot rebase` will rebase this PR
- `@dependabot recreate` will recreate this PR, overwriting any edits that have been made to it
- `@dependabot show <dependency name> ignore conditions` will show all of the ignore conditions of the specified dependency
- `@dependabot ignore this major version` will close this PR and stop Dependabot creating any more for this major version (unless you reopen the PR or upgrade to it yourself)
- `@dependabot ignore this minor version` will close this PR and stop Dependabot creating any more for this minor version (unless you reopen the PR or upgrade to it yourself)
- `@dependabot ignore this dependency` will close this PR and stop Dependabot creating any more for this dependency (unless you reopen the PR or upgrade to it yourself)
You can disable automated security fix PRs for this repo from the [Security Alerts page](https://github.com/nathanpond/AutoNate/network/alerts).

</details>

---

## archived-152 — Migrate BlockNote 0.51 → 0.54 (Yjs decoupling) across SPA and hocuspocus

`MERGED (merged 2026-08-31)` · nathanpond · opened 2026-08-31 · `feat/blocknote-0.54` → `master`

Closes archived-150

## What
BlockNote 0.52 decoupled Yjs from `@blocknote/core`, which is what broke every Dependabot SPA/hocuspocus group so far. This lands 0.54.0 on both sides in lock-step.

- `YjsThreadStore` → `@blocknote/core/yjs`; `User` → `@blocknote/core`; editor options wrapped in `withCollaboration(...)`. Three files in `src/lib/yjs/`, no behaviour change; same Y.Doc fragment/threads map.
- `@tiptap/core` `overrides` pin **removed** — 0.54 needs `^3.29.2`; the tree now resolves a single 3.30.5 by itself (the old pin lacked `createWidgetDecoration` and broke the Vite build).
- `y-prosemirror` added as a direct SPA dependency (docx-editor imports it; BlockNote only peers it now).
- hocuspocus `@blocknote/core` + `server-util` → 0.54.0 (`yDocToBlocks` unchanged).

## Verification
- [x] SPA `tsc -b`, `eslint`, `vite build` clean; hocuspocus `tsc` clean; single `@tiptap/core` in the tree.
- [x] Full Playwright suite against a rebuilt hocuspocus image (server-util 0.54.0): **140 passed / 0 failed / 2 skipped** (the two known skips).
- [x] Existing dev page (`Hawaii: The Aloha State` — headings, tables, lists, bold/italic) opens with **zero console errors**, Yjs tickets issued, edit mode + formatting toolbar with **Add comment** for the editor role.
- [x] Comment thread created/resolved/deleted through the migrated `YjsThreadStore` + `wrapThreadStoreWithAuditing` proxy (0 → 1 → 0 threads).

## Found on the way
- **archived-151** — the audit POST `/api/yjs/comment-event` has *always* returned 400: the SPA sends `documentName`, the endpoint requires `pageId` (both from commit `1b0cd589`). Not caused here; filed separately.
- No E2E spec exercises BlockNote comment threads (the "comment" specs are record comments); the composer is timing-sensitive under automation. Worth a spec once archived-151 is fixed.

## Follow-ups
- Dependabot archived-104 (hocuspocus group: `@hocuspocus/server` 4.6, pg, yjs) can now land; pair with a `@hocuspocus/provider` 4.6 bump on the SPA.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Y5ie3qTEptr4MjYw5i6a5F

---

## archived-153 — Bump the hocuspocus-minor-patch group across 1 directory with 8 updates

`CLOSED` · app/dependabot · opened 2026-08-31 · `dependabot/npm_and_yarn/services/hocuspocus/hocuspocus-minor-patch-c19589bf8c` → `master`

Bumps the hocuspocus-minor-patch group with 8 updates in the /services/hocuspocus directory:

| Package | From | To |
| --- | --- | --- |
| [@hocuspocus/server](https://github.com/ueberdosis/hocuspocus) | `4.0.0` | `4.6.0` |
| [pg](https://github.com/brianc/node-postgres/tree/HEAD/packages/pg) | `8.20.0` | `8.23.0` |
| [@types/pg](https://github.com/DefinitelyTyped/DefinitelyTyped/tree/HEAD/types/pg) | `8.20.0` | `8.23.1` |
| [react](https://github.com/react/react/tree/HEAD/packages/react) | `19.2.6` | `19.2.8` |
| [@types/react](https://github.com/DefinitelyTyped/DefinitelyTyped/tree/HEAD/types/react) | `19.2.14` | `19.2.18` |
| [react-dom](https://github.com/react/react/tree/HEAD/packages/react-dom) | `19.2.6` | `19.2.8` |
| [@types/react-dom](https://github.com/DefinitelyTyped/DefinitelyTyped/tree/HEAD/types/react-dom) | `19.2.3` | `19.2.5` |
| [yjs](https://github.com/yjs/yjs) | `13.6.30` | `13.6.32` |


Updates `@hocuspocus/server` from 4.0.0 to 4.6.0
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/ueberdosis/hocuspocus/releases">@​hocuspocus/server's releases</a>.</em></p>
<blockquote>
<h2>v4.6.0</h2>
<p>extension-redis will now slightly (setImmediate) delay forwarding messages to Redis, which improves performance a lot when many (500+) users are connected to the same document.</p>
<h2>What's Changed</h2>
<ul>
<li>feat/redis pending flushes by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1135">ueberdosis/hocuspocus#1135</a></li>
<li>fix: encode stateless message once when received operation via Redis … by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1136">ueberdosis/hocuspocus#1136</a></li>
</ul>
<p><strong>Full Changelog</strong>: <a href="https://github.com/ueberdosis/hocuspocus/compare/v4.5.0...v4.6.0">https://github.com/ueberdosis/hocuspocus/compare/v4.5.0...v4.6.0</a></p>
<h2>v4.5.0</h2>
<h2>What's Changed</h2>
<ul>
<li>feat: batch updates before sending to clients by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1130">ueberdosis/hocuspocus#1130</a></li>
<li>fix: ignore message in awarenessUpdateHandler if origin=this by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1129">ueberdosis/hocuspocus#1129</a></li>
<li>fix: when beforeHandleMessage throws, we don't want to process other messages that were already queued by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1123">ueberdosis/hocuspocus#1123</a></li>
</ul>
<p><strong>Full Changelog</strong>: <a href="https://github.com/ueberdosis/hocuspocus/compare/v4.4.0...v4.5.0">https://github.com/ueberdosis/hocuspocus/compare/v4.4.0...v4.5.0</a></p>
<h2>v4.4.0</h2>
<h2>What's Changed</h2>
<ul>
<li>feat: add <code>flushDelay</code> option for batching updates to reduce websocket traffic during heavy editing by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1118">ueberdosis/hocuspocus#1118</a></li>
<li>feat: add consistent state synchronization across Redis instances by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1119">ueberdosis/hocuspocus#1119</a></li>
<li>fix: make sure server.destroy() only runs once by <a href="https://github.com/DefV"><code>@​DefV</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1114">ueberdosis/hocuspocus#1114</a></li>
<li>fix: allow binding the server to a specific address by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1121">ueberdosis/hocuspocus#1121</a></li>
<li>build(deps): bump actions/checkout from 6 to 7 by <a href="https://github.com/dependabot"><code>@​dependabot</code></a>[bot] in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1117">ueberdosis/hocuspocus#1117</a></li>
<li>build(deps): bump hono from 4.12.21 to 4.12.25 by <a href="https://github.com/dependabot"><code>@​dependabot</code></a>[bot] in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1116">ueberdosis/hocuspocus#1116</a></li>
<li>build(deps): bump ws from 8.19.0 to 8.21.0 by <a href="https://github.com/dependabot"><code>@​dependabot</code></a>[bot] in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1115">ueberdosis/hocuspocus#1115</a></li>
</ul>
<h2>New Contributors</h2>
<ul>
<li><a href="https://github.com/DefV"><code>@​DefV</code></a> made their first contribution in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1114">ueberdosis/hocuspocus#1114</a></li>
</ul>
<p><strong>Full Changelog</strong>: <a href="https://github.com/ueberdosis/hocuspocus/compare/v4.3.0...v4.4.0">https://github.com/ueberdosis/hocuspocus/compare/v4.3.0...v4.4.0</a></p>
<h2>v4.3.0</h2>
<h2>What's Changed</h2>
<ul>
<li>feat: add <code>afterHandleMessage</code> hook to run after message handling completion by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1112">ueberdosis/hocuspocus#1112</a></li>
<li>feat: enforce pre-auth resource limits to safeguard server stability by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1113">ueberdosis/hocuspocus#1113</a></li>
</ul>
<p><strong>Full Changelog</strong>: <a href="https://github.com/ueberdosis/hocuspocus/compare/v4.2.0...v4.3.0">https://github.com/ueberdosis/hocuspocus/compare/v4.2.0...v4.3.0</a></p>
<h2>v4.2.0</h2>
<h2>What's Changed</h2>
<ul>
<li>feat: add <code>unloadImmediately</code> option to <code>disconnect()</code> for configurable document persistence behavior by <a href="https://github.com/janthurau"><code>@​janthurau</code></a> in <a href="https://redirect.github.com/ueberdosis/hocuspocus/pull/1111">ueberdosis/hocuspocus#1111</a></li>
</ul>
<p><strong>Full Changelog</strong>: <a href="https://github.com/ueberdosis/hocuspocus/compare/v4.1.2...v4.2.0">https://github.com/ueberdosis/hocuspocus/compare/v4.1.2...v4.2.0</a></p>
<h2>v4.1.2</h2>
<h2>What's Changed</h2>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Changelog</summary>
<p><em>Sourced from <a href="https://github.com/ueberdosis/hocuspocus/blob/main/CHANGELOG.md">@​hocuspocus/server's changelog</a>.</em></p>
<blockquote>
<h1><a href="https://github.com/ueberdosis/hocuspocus/compare/v4.5.0...v4.6.0">4.6.0</a> (2026-08-10)</h1>
<h3>Bug Fixes</h3>
<ul>
<li>encode stateless message once when received operation via Redis ; this is a performance fix. (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1136">#1136</a>) (<a href="https://github.com/ueberdosis/hocuspocus/commit/b524b4b30299a64ffa1309f70a0fd6e761103d4a">b524b4b</a>)</li>
</ul>
<h1><a href="https://github.com/ueberdosis/hocuspocus/compare/v4.4.0...v4.5.0">4.5.0</a> (2026-08-04)</h1>
<h3>Bug Fixes</h3>
<ul>
<li>audit (<a href="https://github.com/ueberdosis/hocuspocus/commit/141360c256022deb5578c3902c3dfe0af8f6516e">141360c</a>)</li>
<li>flawky test relying on timings (<a href="https://github.com/ueberdosis/hocuspocus/commit/fe4a8e68801f1659624f53da745e595ad9f11c63">fe4a8e6</a>)</li>
<li>ignore message in awarenessUpdateHandler if origin=this (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1129">#1129</a>) (<a href="https://github.com/ueberdosis/hocuspocus/commit/08b25d4b258d932c68c999c14edcb4efc65c7a9b">08b25d4</a>)</li>
<li>update packages via audit --fix (<a href="https://github.com/ueberdosis/hocuspocus/commit/1dc9ca0ff35f1033136473d134cee8cb6b336281">1dc9ca0</a>)</li>
<li>when beforeHandleMessage throws, we don't want to process other messages that were already queued (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1123">#1123</a>) (<a href="https://github.com/ueberdosis/hocuspocus/commit/ed5dc40581cc829a6d0b04040717a8ee89296140">ed5dc40</a>)</li>
</ul>
<h3>Features</h3>
<ul>
<li>pnpm11 (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1133">#1133</a>) (<a href="https://github.com/ueberdosis/hocuspocus/commit/01c224ad9133340048c0e4f7bdce3981f4984d76">01c224a</a>)</li>
</ul>
<h1><a href="https://github.com/ueberdosis/hocuspocus/compare/v4.3.0...v4.4.0">4.4.0</a> (2026-07-13)</h1>
<h3>Bug Fixes</h3>
<ul>
<li>allow binding the server to a specific address (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1121">#1121</a>) (<a href="https://github.com/ueberdosis/hocuspocus/commit/408127b1c090356cc9148a801f314a8e6f863b09">408127b</a>)</li>
</ul>
<h3>Features</h3>
<ul>
<li>add <code>flushDelay</code> option for batching updates to reduce websocket traffic during heavy editing (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1118">#1118</a>) (<a href="https://github.com/ueberdosis/hocuspocus/commit/75594c05d57d48f2f70d4c9440c28b8226bf95ac">75594c0</a>)</li>
<li>add consistent state synchronization across Redis instances (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1119">#1119</a>) (<a href="https://github.com/ueberdosis/hocuspocus/commit/0051a6cb7618290d1f574da7ad61da2be77f839d">0051a6c</a>)</li>
</ul>
<h1><a href="https://github.com/ueberdosis/hocuspocus/compare/v4.2.0...v4.3.0">4.3.0</a> (2026-06-18)</h1>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/5c85b91af99544630200c438bfc5594a574d912e"><code>5c85b91</code></a> v4.6.0</li>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/d55367e6d3c20167d1daf920aa1e1094909a58ba"><code>d55367e</code></a> Feat/redis pending flushes (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1135">#1135</a>)</li>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/b524b4b30299a64ffa1309f70a0fd6e761103d4a"><code>b524b4b</code></a> fix: encode stateless message once when received operation via Redis ; this i...</li>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/3ec608445b8e024e15759504cca9ff1f7b09edf8"><code>3ec6084</code></a> build(deps): bump pnpm/action-setup from 5 to 6.0.9 (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1131">#1131</a>)</li>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/7827bded7c9181513a3b7c94acbaee0e4059d066"><code>7827bde</code></a> v4.5.0</li>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/141360c256022deb5578c3902c3dfe0af8f6516e"><code>141360c</code></a> fix: audit</li>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/1dc9ca0ff35f1033136473d134cee8cb6b336281"><code>1dc9ca0</code></a> fix: update packages via audit --fix</li>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/01c224ad9133340048c0e4f7bdce3981f4984d76"><code>01c224a</code></a> feat: pnpm11 (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1133">#1133</a>)</li>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/d9f87a6b738afa718dc0dd47580e02eacc764ce8"><code>d9f87a6</code></a> Feat/batch updates before sending to clients (<a href="https://redirect.github.com/ueberdosis/hocuspocus/issues/1130">#1130</a>)</li>
<li><a href="https://github.com/ueberdosis/hocuspocus/commit/a5812e6ec2fbeeefd6dd388a39e1d16fd192f6db"><code>a5812e6</code></a> chore: sync default port with playground</li>
<li>Additional commits viewable in <a href="https://github.com/ueberdosis/hocuspocus/compare/v4.0.0...v4.6.0">compare view</a></li>
</ul>
</details>
<br />

Updates `pg` from 8.20.0 to 8.23.0
<details>
<summary>Changelog</summary>
<p><em>Sourced from <a href="https://github.com/brianc/node-postgres/blob/master/CHANGELOG.md">pg's changelog</a>.</em></p>
<blockquote>
<h2>pg@8.23.0</h2>
<ul>
<li>Add support for query <a href="https://redirect.github.com/brianc/node-postgres/pull/3652"><code>pipelineing</code></a>.</li>
</ul>
<h2>pg@8.22.0</h2>
<ul>
<li>Add support for <a href="https://redirect.github.com/brianc/node-postgres/pull/3688">sslnegotiation=direct</a> for PostgreSQL 17+.</li>
</ul>
<h2>pg@8.21.0</h2>
<ul>
<li>Handle <a href="https://redirect.github.com/brianc/node-postgres/pull/3521">SASL SCRAM</a> server error responses properly.</li>
<li>Add support for <a href="https://redirect.github.com/brianc/node-postgres/pull/3667">node@26</a>.</li>
<li>Add <code>scramMaxIterations</code> <a href="https://redirect.github.com/brianc/node-postgres/pull/3677">config option</a>.</li>
<li>Add <code>client.getTransactionStatus()</code> <a href="https://redirect.github.com/brianc/node-postgres/pull/3645">method</a>.</li>
</ul>
</blockquote>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/brianc/node-postgres/commit/df274d1ba9ad9d11a8f1079314faeafde7208207"><code>df274d1</code></a> Publish</li>
<li><a href="https://github.com/brianc/node-postgres/commit/eb19d0fe6d7da11e7f1c5e73e4026350e42f9156"><code>eb19d0f</code></a> Add opt-in query pipelining (<a href="https://github.com/brianc/node-postgres/tree/HEAD/packages/pg/issues/3652">#3652</a>)</li>
<li><a href="https://github.com/brianc/node-postgres/commit/b617619f9fb6fbd231731823e2732a2927ded4be"><code>b617619</code></a> Publish</li>
<li><a href="https://github.com/brianc/node-postgres/commit/d80b2612fbe83ed8234637f20b943d85e4331094"><code>d80b261</code></a> Update docs &amp; changelog</li>
<li><a href="https://github.com/brianc/node-postgres/commit/835fb83ab9e1cf30fa8367ba42bd633720d71832"><code>835fb83</code></a> Fix error handling for exceptions on values parsing. (<a href="https://github.com/brianc/node-postgres/tree/HEAD/packages/pg/issues/3574">#3574</a>)</li>
<li><a href="https://github.com/brianc/node-postgres/commit/f49ab4a9795ae0866409f9bfe52a68b4f65ef024"><code>f49ab4a</code></a> fix: correct spelling mistakes across codebase (<a href="https://github.com/brianc/node-postgres/tree/HEAD/packages/pg/issues/3692">#3692</a>)</li>
<li><a href="https://github.com/brianc/node-postgres/commit/d7175a4aa0347b7416109e9ecc61d4d235486d0e"><code>d7175a4</code></a> Expand CI matrix of PG versions and add direct SSL test (<a href="https://github.com/brianc/node-postgres/tree/HEAD/packages/pg/issues/3693">#3693</a>)</li>
<li><a href="https://github.com/brianc/node-postgres/commit/882fc308cce7bf136cd1448e00395f760dad3e00"><code>882fc30</code></a> Add support for sslnegotiation=direct (PostgreSQL 17) (<a href="https://github.com/brianc/node-postgres/tree/HEAD/packages/pg/issues/3688">#3688</a>)</li>
<li><a href="https://github.com/brianc/node-postgres/commit/544b1ce8152bc280e398dc1e8a66920abe6a640e"><code>544b1ce</code></a> Publish</li>
<li><a href="https://github.com/brianc/node-postgres/commit/cc03fa5cdf0f1e67b2518ebad5cf2269206aa49c"><code>cc03fa5</code></a> Add scramMaxIterations option to limit SCRAM iteration count (<a href="https://github.com/brianc/node-postgres/tree/HEAD/packages/pg/issues/3677">#3677</a>)</li>
<li>Additional commits viewable in <a href="https://github.com/brianc/node-postgres/commits/pg@8.23.0/packages/pg">compare view</a></li>
</ul>
</details>
<br />

Updates `@types/pg` from 8.20.0 to 8.23.1
<details>
<summary>Commits</summary>
<ul>
<li>See full diff in <a href="https://github.com/DefinitelyTyped/DefinitelyTyped/commits/HEAD/types/pg">compare view</a></li>
</ul>
</details>
<br />

Updates `react` from 19.2.6 to 19.2.8
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/react/react/releases">react's releases</a>.</em></p>
<blockquote>
<h2>19.2.8 (July 21st, 2026)</h2>
<h2>React Server Components</h2>
<ul>
<li>Performance improvements when decoding
(<a href="https://redirect.github.com/facebook/react/pull/37087">#37087</a> by <a href="https://github.com/eps1lon"><code>@​eps1lon</code></a>)</li>
</ul>
<h2>19.2.7 (June 1st, 2026)</h2>
<h2>React Server Components</h2>
<ul>
<li>Fixed missing <code>FormData</code> entries in Server Actions which regressed in 19.2.6
(<a href="https://redirect.github.com/facebook/react/pull/36566">#36566</a> by <a href="https://github.com/unstubbable"><code>@​unstubbable</code></a>)</li>
</ul>
</blockquote>
</details>
<details>
<summary>Changelog</summary>
<p><em>Sourced from <a href="https://github.com/react/react/blob/main/CHANGELOG.md">react's changelog</a>.</em></p>
<blockquote>
<h2>19.2.7 (June 1, 2026)</h2>
<h3>React Server Components</h3>
<ul>
<li>Fixed missing <code>FormData</code> entries in Server Actions which regressed in 19.2.6 (<a href="https://github.com/unstubbable"><code>@​unstubbable</code></a> <a href="https://redirect.github.com/facebook/react/pull/36566">#36566</a>)</li>
</ul>
</blockquote>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/react/react/commit/1dd4ecbdabf826f527fc9a58c05ea70375b7d170"><code>1dd4ecb</code></a> [FlightReply] Performance improvements when decoding (<a href="https://github.com/react/react/tree/HEAD/packages/react/issues/37087">#37087</a>)</li>
<li><a href="https://github.com/react/react/commit/b0d2fdb78bdfae075a7fa02ddcebbf25f90952c2"><code>b0d2fdb</code></a> [19.2.x] Update required references to GitHub repo (<a href="https://github.com/react/react/tree/HEAD/packages/react/issues/36753">#36753</a>)</li>
<li><a href="https://github.com/react/react/commit/6117d7cca4906492c51fe6a03381e35adfd86e7d"><code>6117d7c</code></a> Version 19.2.7 (<a href="https://github.com/react/react/tree/HEAD/packages/react/issues/36591">#36591</a>)</li>
<li>See full diff in <a href="https://github.com/react/react/commits/v19.2.8/packages/react">compare view</a></li>
</ul>
</details>
<details>
<summary>Maintainer changes</summary>
<p>This version was pushed to npm by <a href="https://www.npmjs.com/~GitHub%20Actions">GitHub Actions</a>, a new releaser for react since your current version.</p>
</details>
<br />

Updates `@types/react` from 19.2.14 to 19.2.18
<details>
<summary>Commits</summary>
<ul>
<li>See full diff in <a href="https://github.com/DefinitelyTyped/DefinitelyTyped/commits/HEAD/types/react">compare view</a></li>
</ul>
</details>
<br />

Updates `react-dom` from 19.2.6 to 19.2.8
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/react/react/releases">react-dom's releases</a>.</em></p>
<blockquote>
<h2>19.2.8 (July 21st, 2026)</h2>
<h2>React Server Components</h2>
<ul>
<li>Performance improvements when decoding
(<a href="https://redirect.github.com/facebook/react/pull/37087">#37087</a> by <a href="https://github.com/eps1lon"><code>@​eps1lon</code></a>)</li>
</ul>
<h2>19.2.7 (June 1st, 2026)</h2>
<h2>React Server Components</h2>
<ul>
<li>Fixed missing <code>FormData</code> entries in Server Actions which regressed in 19.2.6
(<a href="https://redirect.github.com/facebook/react/pull/36566">#36566</a> by <a href="https://github.com/unstubbable"><code>@​unstubbable</code></a>)</li>
</ul>
</blockquote>
</details>
<details>
<summary>Changelog</summary>
<p><em>Sourced from <a href="https://github.com/react/react/blob/main/CHANGELOG.md">react-dom's changelog</a>.</em></p>
<blockquote>
<h2>19.2.7 (June 1, 2026)</h2>
<h3>React Server Components</h3>
<ul>
<li>Fixed missing <code>FormData</code> entries in Server Actions which regressed in 19.2.6 (<a href="https://github.com/unstubbable"><code>@​unstubbable</code></a> <a href="https://redirect.github.com/facebook/react/pull/36566">#36566</a>)</li>
</ul>
</blockquote>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/react/react/commit/1dd4ecbdabf826f527fc9a58c05ea70375b7d170"><code>1dd4ecb</code></a> [FlightReply] Performance improvements when decoding (<a href="https://github.com/react/react/tree/HEAD/packages/react-dom/issues/37087">#37087</a>)</li>
<li><a href="https://github.com/react/react/commit/b0d2fdb78bdfae075a7fa02ddcebbf25f90952c2"><code>b0d2fdb</code></a> [19.2.x] Update required references to GitHub repo (<a href="https://github.com/react/react/tree/HEAD/packages/react-dom/issues/36753">#36753</a>)</li>
<li><a href="https://github.com/react/react/commit/6117d7cca4906492c51fe6a03381e35adfd86e7d"><code>6117d7c</code></a> Version 19.2.7 (<a href="https://github.com/react/react/tree/HEAD/packages/react-dom/issues/36591">#36591</a>)</li>
<li>See full diff in <a href="https://github.com/react/react/commits/v19.2.8/packages/react-dom">compare view</a></li>
</ul>
</details>
<details>
<summary>Maintainer changes</summary>
<p>This version was pushed to npm by <a href="https://www.npmjs.com/~GitHub%20Actions">GitHub Actions</a>, a new releaser for react-dom since your current version.</p>
</details>
<br />

Updates `@types/react-dom` from 19.2.3 to 19.2.5
<details>
<summary>Commits</summary>
<ul>
<li>See full diff in <a href="https://github.com/DefinitelyTyped/DefinitelyTyped/commits/HEAD/types/react-dom">compare view</a></li>
</ul>
</details>
<br />

Updates `yjs` from 13.6.30 to 13.6.32
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/yjs/yjs/releases">yjs's releases</a>.</em></p>
<blockquote>
<h2>v13.6.32</h2>
<ul>
<li>fix <a href="https://redirect.github.com/yjs/yjs/issues/797">#797</a> - undomanager clears destroy handler  95e890d9</li>
</ul>
<hr />
<p><a href="https://github.com/yjs/yjs/compare/v13.6.31...v13.6.32">https://github.com/yjs/yjs/compare/v13.6.31...v13.6.32</a></p>
<h2>v13.6.31</h2>
<ul>
<li>Merge branch &amp;<a href="https://redirect.github.com/yjs/yjs/issues/39">#39</a>;ppiotrowicz-fix/757-undo-attr-redo&amp;<a href="https://redirect.github.com/yjs/yjs/issues/39">#39</a>; into v13  1ddba7e4</li>
<li>fix <a href="https://redirect.github.com/yjs/yjs/issues/757">#757</a> in v13  d9aaff72</li>
<li>fix undoing setAttribute combined with delete corrupts remote state - closes <a href="https://redirect.github.com/yjs/yjs/issues/757">#757</a>  67c809ee</li>
</ul>
<hr />
<p><a href="https://github.com/yjs/yjs/compare/v13.6.30...v13.6.31">https://github.com/yjs/yjs/compare/v13.6.30...v13.6.31</a></p>
</blockquote>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/yjs/yjs/commit/1ce38f75f786e4bc0b2cc9703afbc6eea8fe7859"><code>1ce38f7</code></a> 13.6.32</li>
<li><a href="https://github.com/yjs/yjs/commit/95e890d99ac6b8462fc02722e60b1dbd17c9c29d"><code>95e890d</code></a> fix <a href="https://redirect.github.com/yjs/yjs/issues/797">#797</a> - undomanager clears destroy handler</li>
<li><a href="https://github.com/yjs/yjs/commit/271330889b13eae102873bb417d6747a0ddd8b4a"><code>2713308</code></a> 13.6.31</li>
<li><a href="https://github.com/yjs/yjs/commit/1ddba7e48cfa9cdf4c0c51b2a1bd22986a0e8704"><code>1ddba7e</code></a> Merge branch 'ppiotrowicz-fix/757-undo-attr-redo' into v13</li>
<li><a href="https://github.com/yjs/yjs/commit/d9aaff72b246c0f2a5c07eaa4f685079fe9e6e5a"><code>d9aaff7</code></a> fix <a href="https://redirect.github.com/yjs/yjs/issues/757">#757</a> in v13</li>
<li><a href="https://github.com/yjs/yjs/commit/67c809ee6b787984d7bf709df9900b93cccffb7e"><code>67c809e</code></a> fix undoing setAttribute combined with delete corrupts remote state - closes ...</li>
<li>See full diff in <a href="https://github.com/yjs/yjs/compare/v13.6.30...v13.6.32">compare view</a></li>
</ul>
</details>
<br />

Updates `@types/pg` from 8.20.0 to 8.23.1
<details>
<summary>Commits</summary>
<ul>
<li>See full diff in <a href="https://github.com/DefinitelyTyped/DefinitelyTyped/commits/HEAD/types/pg">compare view</a></li>
</ul>
</details>
<br />

Updates `@types/react` from 19.2.14 to 19.2.18
<details>
<summary>Commits</summary>
<ul>
<li>See full diff in <a href="https://github.com/DefinitelyTyped/DefinitelyTyped/commits/HEAD/types/react">compare view</a></li>
</ul>
</details>
<br />

Updates `@types/react-dom` from 19.2.3 to 19.2.5
<details>
<summary>Commits</summary>
<ul>
<li>See full diff in <a href="https://github.com/DefinitelyTyped/DefinitelyTyped/commits/HEAD/types/react-dom">compare view</a></li>
</ul>
</details>
<br />


Dependabot will resolve any conflicts with this PR as long as you don't alter it yourself. You can also trigger a rebase manually by commenting `@dependabot rebase`.

[//]: # (dependabot-automerge-start)
[//]: # (dependabot-automerge-end)

---

<details>
<summary>Dependabot commands and options</summary>
<br />

You can trigger Dependabot actions by commenting on this PR:
- `@dependabot rebase` will rebase this PR
- `@dependabot recreate` will recreate this PR, overwriting any edits that have been made to it
- `@dependabot show <dependency name> ignore conditions` will show all of the ignore conditions of the specified dependency
- `@dependabot ignore <dependency name> major version` will close this group update PR and stop Dependabot creating any more for the specific dependency's major version (unless you unignore this specific dependency's major version or upgrade to it yourself)
- `@dependabot ignore <dependency name> minor version` will close this group update PR and stop Dependabot creating any more for the specific dependency's minor version (unless you unignore this specific dependency's minor version or upgrade to it yourself)
- `@dependabot ignore <dependency name>` will close this group update PR and stop Dependabot creating any more for the specific dependency (unless you unignore this specific dependency or upgrade to it yourself)
- `@dependabot unignore <dependency name>` will remove all of the ignore conditions of the specified dependency
- `@dependabot unignore <dependency name> <ignore condition>` will remove the ignore condition of the specified dependency and ignore conditions


</details>

<details><summary>Comment — nathanpond, 2026-08-31</summary>

Closing: every update in this group is already on master via archived-156 (`@hocuspocus/server` 4.6.0, pg 8.23.0, `@types/pg` 8.23.1, react/react-dom 19.2.8 + types, yjs 13.6.32) — this PR was opened from a base that predates that merge.

</details>

<details><summary>Comment — dependabot[bot], 2026-08-31</summary>

This pull request was built based on a group rule. Closing it will not ignore any of these versions in future pull requests.

To ignore these dependencies, configure [ignore rules](https://docs.github.com/en/code-security/dependabot/dependabot-version-updates/configuration-options-for-the-dependabot.yml-file#ignore) in dependabot.yml

</details>

---

## archived-155 — Bump the spa-minor-patch group across 1 directory with 29 updates

`CLOSED` · app/dependabot · opened 2026-08-31 · `dependabot/npm_and_yarn/src/AutoNate.Spa/spa-minor-patch-07ff83b63d` → `master`

Bumps the spa-minor-patch group with 29 updates in the /src/AutoNate.Spa directory:

| Package | From | To |
| --- | --- | --- |
| [@codemirror/lang-html](https://github.com/codemirror/lang-html) | `6.4.11` | `6.4.12` |
| [@fortawesome/fontawesome-free](https://github.com/FortAwesome/Font-Awesome) | `7.2.0` | `7.3.1` |
| [@mantine/charts](https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts) | `9.1.1` | `9.5.2` |
| [@mantine/colors-generator](https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator) | `9.1.1` | `9.5.2` |
| [@mantine/core](https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/core) | `9.1.1` | `9.5.2` |
| [@mantine/dates](https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dates) | `9.1.1` | `9.5.2` |
| [@mantine/dropzone](https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dropzone) | `9.1.1` | `9.5.2` |
| [@mantine/form](https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/form) | `9.1.1` | `9.5.2` |
| [@mantine/hooks](https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/hooks) | `9.1.1` | `9.5.2` |
| [@mantine/modals](https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/modals) | `9.1.1` | `9.5.2` |
| [@mantine/notifications](https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/notifications) | `9.1.1` | `9.5.2` |
| [@tanstack/react-query](https://github.com/TanStack/query/tree/HEAD/packages/react-query) | `5.100.1` | `5.102.8` |
| [@tanstack/react-query-devtools](https://github.com/TanStack/query/tree/HEAD/packages/react-query-devtools) | `5.100.1` | `5.102.8` |
| [@uiw/react-codemirror](https://github.com/uiwjs/react-codemirror) | `4.25.9` | `4.25.11` |
| [@xyflow/react](https://github.com/xyflow/xyflow/tree/HEAD/packages/react) | `12.10.2` | `12.11.5` |
| [axios](https://github.com/axios/axios) | `1.18.0` | `1.20.0` |
| [marked](https://github.com/markedjs/marked) | `18.0.4` | `18.0.11` |
| [react](https://github.com/react/react/tree/HEAD/packages/react) | `19.2.5` | `19.2.8` |
| [@types/react](https://github.com/DefinitelyTyped/DefinitelyTyped/tree/HEAD/types/react) | `19.2.14` | `19.2.18` |
| [react-dom](https://github.com/react/react/tree/HEAD/packages/react-dom) | `19.2.5` | `19.2.8` |
| [@types/react-dom](https://github.com/DefinitelyTyped/DefinitelyTyped/tree/HEAD/types/react-dom) | `19.2.3` | `19.2.5` |
| [react-grid-layout](https://github.com/STRML/react-grid-layout) | `2.2.3` | `2.2.4` |
| [@types/react-grid-layout](https://github.com/DefinitelyTyped/DefinitelyTyped/tree/HEAD/types/react-grid-layout) | `1.3.6` | `2.1.0` |
| [recharts](https://github.com/recharts/recharts) | `3.8.1` | `3.10.1` |
| [yjs](https://github.com/yjs/yjs) | `13.6.30` | `13.6.32` |
| [zod](https://github.com/colinhacks/zod) | `4.3.6` | `4.4.3` |
| [@vitejs/plugin-react](https://github.com/vitejs/vite-plugin-react/tree/HEAD/packages/plugin-react) | `6.0.1` | `6.1.1` |
| [globals](https://github.com/sindresorhus/globals) | `17.6.0` | `17.11.0` |
| [typescript-eslint](https://github.com/typescript-eslint/typescript-eslint/tree/HEAD/packages/typescript-eslint) | `8.60.0` | `8.68.0` |


Updates `@codemirror/lang-html` from 6.4.11 to 6.4.12
<details>
<summary>Commits</summary>
<ul>
<li>See full diff in <a href="https://github.com/codemirror/lang-html/commits">compare view</a></li>
</ul>
</details>
<br />

Updates `@fortawesome/fontawesome-free` from 7.2.0 to 7.3.1
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/FortAwesome/Font-Awesome/releases">@​fortawesome/fontawesome-free's releases</a>.</em></p>
<blockquote>
<h2>Release 7.3.1</h2>
<p><strong>Change log available at <a href="https://fontawesome.com/docs/changelog/">https://fontawesome.com/docs/changelog/</a></strong></p>
<h2>Release 7.3.0</h2>
<p><strong>Change log available at <a href="https://fontawesome.com/docs/changelog/">https://fontawesome.com/docs/changelog/</a></strong></p>
</blockquote>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/FortAwesome/Font-Awesome/commit/14c65a3747d0f3b751f15831fc719236aea8729d"><code>14c65a3</code></a> Release 7.3.1 (<a href="https://redirect.github.com/FortAwesome/Font-Awesome/issues/21630">#21630</a>)</li>
<li><a href="https://github.com/FortAwesome/Font-Awesome/commit/70fb2dd154b617f62fc4ae5b0b7e2943bfd2aa96"><code>70fb2dd</code></a> Release 7.3.0 (<a href="https://redirect.github.com/FortAwesome/Font-Awesome/issues/21612">#21612</a>)</li>
<li>See full diff in <a href="https://github.com/FortAwesome/Font-Awesome/compare/7.2.0...7.3.1">compare view</a></li>
</ul>
</details>
<details>
<summary>Maintainer changes</summary>
<p>This version was pushed to npm by <a href="https://www.npmjs.com/~fortawesome-admin">fortawesome-admin</a>, a new releaser for <code>@​fortawesome/fontawesome-free</code> since your current version.</p>
</details>
<br />

Updates `@mantine/charts` from 9.1.1 to 9.5.2
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/mantinedev/mantine/releases">@​mantine/charts's releases</a>.</em></p>
<blockquote>
<h2>9.5.2</h2>
<ul>
<li><code>[@mantine/hooks]</code> use-debounced-value: Fix <code>leading: true</code> firing multiple times per burst and emiting a stale value (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9119">#9119</a>)</li>
<li><code>[@mantine/schedule]</code> Fix recurring events not working with timzones (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9112">#9112</a>)</li>
<li><code>[@mantine/dates]</code> Fix <code>minDate</code> used for default date in some cases (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9117">#9117</a>)</li>
<li><code>[@mantine/core]</code> Tooltip: Fix tooltip setting NaN in top/left position style when event position values cannot be read (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9131">#9131</a>)</li>
<li><code>[@mantine/dates]</code> TimePicker: Fix incorrect focus handling of partially filled hours field (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9128">#9128</a>)</li>
<li><code>[@mantine/core]</code> RollingNumber: Fix incorrect copy event handling (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9132">#9132</a>)</li>
<li><code>[@mantine/core]</code> Notification: Fix incorrect <code>closeButtonProps</code> type (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9134">#9134</a>)</li>
<li><code>[@mantine/code-highlight]</code> Add support for lazy languages loading (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9141">#9141</a>)</li>
<li><code>[@mantine/code-highlight]</code> CodeHighlight: Add prop to keep indentation of the first line of the code block (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9140">#9140</a>)</li>
<li><code>[@mantine/dates]</code> Add missing formatting functions to MiniCalendarm DateInput and YarsList components</li>
<li><code>[@mantine/schedule]</code> WeekView: Improve performance of events positioning algorithm (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9075">#9075</a>)</li>
<li><code>[@mantine/form]</code> Add new useWatchValue hook</li>
<li><code>[@mantine/core]</code> Fix Combobox-based components not working correctly with Chrome autocomplete</li>
</ul>
<h2>9.5.1</h2>
<ul>
<li><code>[@mantine/tiptap]</code> Fix controls being initially disabledbefore element is focused</li>
<li><code>[@mantine/tiptap]</code> Fix source code control wrapping content with extra p tag</li>
<li><code>[@mantine/hooks]</code> use-scroll-spy: Allow usage with refs (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9025">#9025</a>)</li>
<li><code>[@mantine/core]</code> ColorInput: Add support for fullWidth prop (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9061">#9061</a>)</li>
<li><code>[@mantine/core]</code> Checkbox: Fix incottect indeterminate aria attributes handling in Checkbox.Card (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9095">#9095</a>)</li>
<li><code>[@mantine/core]</code> FloatingIndicator: Fix position and size calculation under scaled ancestors (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9071">#9071</a>)</li>
<li><code>[@mantine/core]</code> Tooltip: Add interactive prop support (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9072">#9072</a>)</li>
<li><code>[@mantine/core]</code> Cascader: Add safe area polygon support</li>
<li><code>[@mantine/core]</code> PasswordInput: Add option to change whether the visibility toggle is focusable (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9090">#9090</a>)</li>
<li><code>[@mantine/charts]</code> ScatterChart: Add option to add second y axis</li>
<li><code>[@mantine/schedule]</code> YearView: Add <code>renderDay</code> prop support</li>
<li><code>[@mantine/schedule]</code> YearView: Add option to hide weekend days</li>
<li><code>[@mantine/core]</code> InputWrapper: Fix <code>component: div</code> triggering typescript error if passed to <code>descriptionProps</code></li>
<li><code>[@mantine/schedule]</code> ResourcesMonthView: Add option to resize events</li>
<li><code>[@mantine/core]</code> FloatingWindow: Add support for  <code>onSizeChange</code> and <code>onResizeStart</code> props (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9085">#9085</a>)</li>
</ul>
<h2>9.5.0 🤖</h2>
<p><a href="https://mantine.dev/changelog/9-5-0">View changelog with demos on mantine.dev website</a></p>
<h2>Support Mantine development</h2>
<p>You can now sponsor Mantine development with <a href="https://opencollective.com/mantinedev">OpenCollective</a>.
All funds are used to improve Mantine and create new features and components.</p>
<h2>Migration to oxc</h2>
<p>Mantine has migrated its linting and formatting toolchain from ESLint and Prettier
to <a href="https://oxc.rs">oxc</a> – <a href="https://www.npmjs.com/package/oxlint">oxlint</a> is now used
as the linter and <a href="https://www.npmjs.com/package/oxfmt">oxfmt</a> as the formatter. Both
tools are written in Rust and are significantly faster than their predecessors, which
makes linting and formatting the entire codebase almost instant.</p>
<p>The shared configuration is available as a new
<a href="https://mantine.dev/oxc-config-mantine">oxc-config-mantine</a> package (a replacement for the previous</p>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/mantinedev/mantine/commit/8a284e2c2c53a9cb6f39f5dc389bf41b7a2073f8"><code>8a284e2</code></a> [release] Version: 9.5.2</li>
<li><a href="https://github.com/mantinedev/mantine/commit/0f57eaf5ae90c9e870fbb2a4cdd61a1d58c4c01d"><code>0f57eaf</code></a> [release] Version: 9.5.1</li>
<li><a href="https://github.com/mantinedev/mantine/commit/1e120595fdde5a414616df908bb3e600021d092e"><code>1e12059</code></a> [<code>@​mantine/charts</code>] ScatterChart: Add option to add second y axis</li>
<li><a href="https://github.com/mantinedev/mantine/commit/ca9bc6f156b63f1a10918d94ec31ec18e4e60546"><code>ca9bc6f</code></a> [release] Version: 9.5.1-alpha.1</li>
<li><a href="https://github.com/mantinedev/mantine/commit/8f1ad1bbe545c9cafafc5aef5b059d3d48e676a6"><code>8f1ad1b</code></a> [release] Version: 9.5.1-alpha.0</li>
<li><a href="https://github.com/mantinedev/mantine/commit/f1d330613f54dc9319d176e6d8ba5ebff233da18"><code>f1d3306</code></a> [release] Version: 9.5.0</li>
<li><a href="https://github.com/mantinedev/mantine/commit/732056219a0283f5822001981d7f652e632c4c87"><code>7320562</code></a> [release] Version: 9.4.3</li>
<li><a href="https://github.com/mantinedev/mantine/commit/170c45a5feed2386a464a7f05ae3daf6379cea04"><code>170c45a</code></a> Merge branch '9.5'</li>
<li><a href="https://github.com/mantinedev/mantine/commit/de21a8203060ba29441ab7623244339748e4319d"><code>de21a82</code></a> [release] Version: 9.4.3-alpha.0</li>
<li><a href="https://github.com/mantinedev/mantine/commit/e5752de4067bd58f6cdd970660b3c8469a56d4e5"><code>e5752de</code></a> [release] Version: 9.4.2</li>
<li>Additional commits viewable in <a href="https://github.com/mantinedev/mantine/commits/9.5.2/packages/@mantine/charts">compare view</a></li>
</ul>
</details>
<details>
<summary>Maintainer changes</summary>
<p>This version was pushed to npm by <a href="https://www.npmjs.com/~GitHub%20Actions">GitHub Actions</a>, a new releaser for <code>@​mantine/charts</code> since your current version.</p>
</details>
<br />

Updates `@mantine/colors-generator` from 9.1.1 to 9.5.2
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/mantinedev/mantine/releases">@​mantine/colors-generator's releases</a>.</em></p>
<blockquote>
<h2>9.5.2</h2>
<ul>
<li><code>[@mantine/hooks]</code> use-debounced-value: Fix <code>leading: true</code> firing multiple times per burst and emiting a stale value (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9119">#9119</a>)</li>
<li><code>[@mantine/schedule]</code> Fix recurring events not working with timzones (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9112">#9112</a>)</li>
<li><code>[@mantine/dates]</code> Fix <code>minDate</code> used for default date in some cases (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9117">#9117</a>)</li>
<li><code>[@mantine/core]</code> Tooltip: Fix tooltip setting NaN in top/left position style when event position values cannot be read (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9131">#9131</a>)</li>
<li><code>[@mantine/dates]</code> TimePicker: Fix incorrect focus handling of partially filled hours field (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9128">#9128</a>)</li>
<li><code>[@mantine/core]</code> RollingNumber: Fix incorrect copy event handling (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9132">#9132</a>)</li>
<li><code>[@mantine/core]</code> Notification: Fix incorrect <code>closeButtonProps</code> type (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9134">#9134</a>)</li>
<li><code>[@mantine/code-highlight]</code> Add support for lazy languages loading (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9141">#9141</a>)</li>
<li><code>[@mantine/code-highlight]</code> CodeHighlight: Add prop to keep indentation of the first line of the code block (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9140">#9140</a>)</li>
<li><code>[@mantine/dates]</code> Add missing formatting functions to MiniCalendarm DateInput and YarsList components</li>
<li><code>[@mantine/schedule]</code> WeekView: Improve performance of events positioning algorithm (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9075">#9075</a>)</li>
<li><code>[@mantine/form]</code> Add new useWatchValue hook</li>
<li><code>[@mantine/core]</code> Fix Combobox-based components not working correctly with Chrome autocomplete</li>
</ul>
<h2>9.5.1</h2>
<ul>
<li><code>[@mantine/tiptap]</code> Fix controls being initially disabledbefore element is focused</li>
<li><code>[@mantine/tiptap]</code> Fix source code control wrapping content with extra p tag</li>
<li><code>[@mantine/hooks]</code> use-scroll-spy: Allow usage with refs (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9025">#9025</a>)</li>
<li><code>[@mantine/core]</code> ColorInput: Add support for fullWidth prop (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9061">#9061</a>)</li>
<li><code>[@mantine/core]</code> Checkbox: Fix incottect indeterminate aria attributes handling in Checkbox.Card (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9095">#9095</a>)</li>
<li><code>[@mantine/core]</code> FloatingIndicator: Fix position and size calculation under scaled ancestors (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9071">#9071</a>)</li>
<li><code>[@mantine/core]</code> Tooltip: Add interactive prop support (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9072">#9072</a>)</li>
<li><code>[@mantine/core]</code> Cascader: Add safe area polygon support</li>
<li><code>[@mantine/core]</code> PasswordInput: Add option to change whether the visibility toggle is focusable (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9090">#9090</a>)</li>
<li><code>[@mantine/charts]</code> ScatterChart: Add option to add second y axis</li>
<li><code>[@mantine/schedule]</code> YearView: Add <code>renderDay</code> prop support</li>
<li><code>[@mantine/schedule]</code> YearView: Add option to hide weekend days</li>
<li><code>[@mantine/core]</code> InputWrapper: Fix <code>component: div</code> triggering typescript error if passed to <code>descriptionProps</code></li>
<li><code>[@mantine/schedule]</code> ResourcesMonthView: Add option to resize events</li>
<li><code>[@mantine/core]</code> FloatingWindow: Add support for  <code>onSizeChange</code> and <code>onResizeStart</code> props (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9085">#9085</a>)</li>
</ul>
<h2>9.5.0 🤖</h2>
<p><a href="https://mantine.dev/changelog/9-5-0">View changelog with demos on mantine.dev website</a></p>
<h2>Support Mantine development</h2>
<p>You can now sponsor Mantine development with <a href="https://opencollective.com/mantinedev">OpenCollective</a>.
All funds are used to improve Mantine and create new features and components.</p>
<h2>Migration to oxc</h2>
<p>Mantine has migrated its linting and formatting toolchain from ESLint and Prettier
to <a href="https://oxc.rs">oxc</a> – <a href="https://www.npmjs.com/package/oxlint">oxlint</a> is now used
as the linter and <a href="https://www.npmjs.com/package/oxfmt">oxfmt</a> as the formatter. Both
tools are written in Rust and are significantly faster than their predecessors, which
makes linting and formatting the entire codebase almost instant.</p>
<p>The shared configuration is available as a new
<a href="https://mantine.dev/oxc-config-mantine">oxc-config-mantine</a> package (a replacement for the previous</p>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/mantinedev/mantine/commit/8a284e2c2c53a9cb6f39f5dc389bf41b7a2073f8"><code>8a284e2</code></a> [release] Version: 9.5.2</li>
<li><a href="https://github.com/mantinedev/mantine/commit/0f57eaf5ae90c9e870fbb2a4cdd61a1d58c4c01d"><code>0f57eaf</code></a> [release] Version: 9.5.1</li>
<li><a href="https://github.com/mantinedev/mantine/commit/ca9bc6f156b63f1a10918d94ec31ec18e4e60546"><code>ca9bc6f</code></a> [release] Version: 9.5.1-alpha.1</li>
<li><a href="https://github.com/mantinedev/mantine/commit/8f1ad1bbe545c9cafafc5aef5b059d3d48e676a6"><code>8f1ad1b</code></a> [release] Version: 9.5.1-alpha.0</li>
<li><a href="https://github.com/mantinedev/mantine/commit/f1d330613f54dc9319d176e6d8ba5ebff233da18"><code>f1d3306</code></a> [release] Version: 9.5.0</li>
<li><a href="https://github.com/mantinedev/mantine/commit/732056219a0283f5822001981d7f652e632c4c87"><code>7320562</code></a> [release] Version: 9.4.3</li>
<li><a href="https://github.com/mantinedev/mantine/commit/de21a8203060ba29441ab7623244339748e4319d"><code>de21a82</code></a> [release] Version: 9.4.3-alpha.0</li>
<li><a href="https://github.com/mantinedev/mantine/commit/e5752de4067bd58f6cdd970660b3c8469a56d4e5"><code>e5752de</code></a> [release] Version: 9.4.2</li>
<li><a href="https://github.com/mantinedev/mantine/commit/d709e0bc277255c2a857f138cc694028273d8697"><code>d709e0b</code></a> [release] Version: 9.4.1</li>
<li><a href="https://github.com/mantinedev/mantine/commit/75d5ab5b419f3aa560bb56fc1d75d7815c5fb2f8"><code>75d5ab5</code></a> [release] Version: 9.4.0</li>
<li>Additional commits viewable in <a href="https://github.com/mantinedev/mantine/commits/9.5.2/packages/@mantine/colors-generator">compare view</a></li>
</ul>
</details>
<details>
<summary>Maintainer changes</summary>
<p>This version was pushed to npm by <a href="https://www.npmjs.com/~GitHub%20Actions">GitHub Actions</a>, a new releaser for <code>@​mantine/colors-generator</code> since your current version.</p>
</details>
<br />

Updates `@mantine/core` from 9.1.1 to 9.5.2
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/mantinedev/mantine/releases">@​mantine/core's releases</a>.</em></p>
<blockquote>
<h2>9.5.2</h2>
<ul>
<li><code>[@mantine/hooks]</code> use-debounced-value: Fix <code>leading: true</code> firing multiple times per burst and emiting a stale value (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/core/issues/9119">#9119</a>)</li>
<li><code>[@mantine/schedule]</code> Fix recurring events not working with timzones (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/core/issues/9112">#9112</a>)</li>
<li><code>[@mantine/dates]</code> Fix <code>minDate</code> used for default date in some cases (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/core/issues/9117">#9117</a>)</li>
<li><code>[@mantine/core]</code> Tooltip: Fix tooltip setting NaN in top/left position style when event position values cannot be read (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/core/issues/9131">#9131</a>)</li>
<li><code>[@mantine/dates]</code> TimePicker: Fix incorrect focus handling of partially filled hours field (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/core/issues/9128">#9128</a>)</li>
<li><code>[@mantine/core]</code> RollingNumber: Fix incorrect copy event handling (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/core/issues/9132">#9132</a>)</li>
<li><code>[@mantine/core]</code> Notification: Fix incorrect <code>closeButtonProps</code> type (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/core/issues/9134">#9134</a>)</li>
<li><code>[@mantine/code-highlight]</code> Add support for lazy languages loading (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/core/issues/9141">#9141</a>)</li>
<li><code>[@mantine/code-highlight]</code> CodeHighlight: Add prop to keep indentation of the first line of the code block (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/core/issues/9140">#9140</a>)</li>
<li><code>[@mantine/dates]</code> Add missing formatting functions to MiniCalendarm DateInput and YarsList components</li>
<li><code>[@mantine/schedule]</code> WeekView: Improve performance of events positioning algorithm (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/core/issues/9075">#9075</a>)</li>
<li><code>[@mantine/form]</code> Add new useWatchValue hook</li>
<li><code>[@mantine/core]</code> Fix Combobox-based components not working correctly with Chrome autocomplete</li>
</ul>
<h2>9.5.1</h2>
<ul>
<li><code>[@mantine/tiptap]</code> Fix controls being initially disabledbefore element is focused</li>
<li><code>[@mantine/tiptap]</code> Fix source code control wrapping content with extra p tag</li>
<li><code>[@mantine/hooks]</code> use-scroll-spy: Allow usage with refs (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/core/issues/9025">#9025</a>)</li>
<li><code>[@mantine/core]</code> ColorInput: Add support for fullWidth prop (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/core/issues/9061">#9061</a>)</li>
<li><code>[@mantine/core]</code> Checkbox: Fix incottect indeterminate aria attributes handling in Checkbox.Card (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/core/issues/9095">#9095</a>)</li>
<li><code>[@mantine/core]</code> FloatingIndicator: Fix position and size calculation under scaled ancestors (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/core/issues/9071">#9071</a>)</li>
<li><code>[@mantine/core]</code> Tooltip: Add interactive prop support (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/core/issues/9072">#9072</a>)</li>
<li><code>[@mantine/core]</code> Cascader: Add safe area polygon support</li>
<li><code>[@mantine/core]</code> PasswordInput: Add option to change whether the visibility toggle is focusable (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/core/issues/9090">#9090</a>)</li>
<li><code>[@mantine/charts]</code> ScatterChart: Add option to add second y axis</li>
<li><code>[@mantine/schedule]</code> YearView: Add <code>renderDay</code> prop support</li>
<li><code>[@mantine/schedule]</code> YearView: Add option to hide weekend days</li>
<li><code>[@mantine/core]</code> InputWrapper: Fix <code>component: div</code> triggering typescript error if passed to <code>descriptionProps</code></li>
<li><code>[@mantine/schedule]</code> ResourcesMonthView: Add option to resize events</li>
<li><code>[@mantine/core]</code> FloatingWindow: Add support for  <code>onSizeChange</code> and <code>onResizeStart</code> props (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/core/issues/9085">#9085</a>)</li>
</ul>
<h2>9.5.0 🤖</h2>
<p><a href="https://mantine.dev/changelog/9-5-0">View changelog with demos on mantine.dev website</a></p>
<h2>Support Mantine development</h2>
<p>You can now sponsor Mantine development with <a href="https://opencollective.com/mantinedev">OpenCollective</a>.
All funds are used to improve Mantine and create new features and components.</p>
<h2>Migration to oxc</h2>
<p>Mantine has migrated its linting and formatting toolchain from ESLint and Prettier
to <a href="https://oxc.rs">oxc</a> – <a href="https://www.npmjs.com/package/oxlint">oxlint</a> is now used
as the linter and <a href="https://www.npmjs.com/package/oxfmt">oxfmt</a> as the formatter. Both
tools are written in Rust and are significantly faster than their predecessors, which
makes linting and formatting the entire codebase almost instant.</p>
<p>The shared configuration is available as a new
<a href="https://mantine.dev/oxc-config-mantine">oxc-config-mantine</a> package (a replacement for the previous</p>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/mantinedev/mantine/commit/8a284e2c2c53a9cb6f39f5dc389bf41b7a2073f8"><code>8a284e2</code></a> [release] Version: 9.5.2</li>
<li><a href="https://github.com/mantinedev/mantine/commit/a2e25fe891429f5ae4aa4c1a4593610a91ae743d"><code>a2e25fe</code></a> [<code>@​mantine/core</code>] Tooltip: Fix tooltip setting NaN in top/left position style w...</li>
<li><a href="https://github.com/mantinedev/mantine/commit/a88b24cf9d379fa3d6cbb31b449e6a5dcc2a789d"><code>a88b24c</code></a> Merge branch 'master' of github.com:mantinedev/mantine</li>
<li><a href="https://github.com/mantinedev/mantine/commit/4509931406a34ca8695a4bb7ff26bd686ffe659d"><code>4509931</code></a> [<code>@​mantine/core</code>] RollingNumber: Fix incorrect copy event handling (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/core/issues/9132">#9132</a>)</li>
<li><a href="https://github.com/mantinedev/mantine/commit/20b9a5e6d1de35f44cf561fd22ff005d07cf656a"><code>20b9a5e</code></a> [<code>@​mantine/core</code>] Notification: Fix incorrect <code>closeButtonProps</code> type (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/core/issues/9134">#9134</a>)</li>
<li><a href="https://github.com/mantinedev/mantine/commit/681c9fee1f17ce77e9c6983d960eeec4c72f4fc2"><code>681c9fe</code></a> [mantine.dev] Fix missing ComboboxPopover styles API documentation (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/core/issues/9135">#9135</a>)</li>
<li><a href="https://github.com/mantinedev/mantine/commit/b30ae5b1aef44945395cbeb40a8f987897e03740"><code>b30ae5b</code></a> [<code>@​mantine/core</code>] Fix Combobox-based components not working correctly with Chro...</li>
<li><a href="https://github.com/mantinedev/mantine/commit/0f57eaf5ae90c9e870fbb2a4cdd61a1d58c4c01d"><code>0f57eaf</code></a> [release] Version: 9.5.1</li>
<li><a href="https://github.com/mantinedev/mantine/commit/58abe86af4153db7639966aba6ad5521b02b1c96"><code>58abe86</code></a> [<code>@​mantine/core</code>] ColorInput: Add support for fullWidth prop (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/core/issues/9061">#9061</a>)</li>
<li><a href="https://github.com/mantinedev/mantine/commit/34db05f19c3115e6b749546e7ae9d07fdf9c4ffe"><code>34db05f</code></a> Merge branch 'master' of github.com:mantinedev/mantine</li>
<li>Additional commits viewable in <a href="https://github.com/mantinedev/mantine/commits/9.5.2/packages/@mantine/core">compare view</a></li>
</ul>
</details>
<details>
<summary>Maintainer changes</summary>
<p>This version was pushed to npm by <a href="https://www.npmjs.com/~GitHub%20Actions">GitHub Actions</a>, a new releaser for <code>@​mantine/core</code> since your current version.</p>
</details>
<br />

Updates `@mantine/dates` from 9.1.1 to 9.5.2
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/mantinedev/mantine/releases">@​mantine/dates's releases</a>.</em></p>
<blockquote>
<h2>9.5.2</h2>
<ul>
<li><code>[@mantine/hooks]</code> use-debounced-value: Fix <code>leading: true</code> firing multiple times per burst and emiting a stale value (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dates/issues/9119">#9119</a>)</li>
<li><code>[@mantine/schedule]</code> Fix recurring events not working with timzones (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dates/issues/9112">#9112</a>)</li>
<li><code>[@mantine/dates]</code> Fix <code>minDate</code> used for default date in some cases (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dates/issues/9117">#9117</a>)</li>
<li><code>[@mantine/core]</code> Tooltip: Fix tooltip setting NaN in top/left position style when event position values cannot be read (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dates/issues/9131">#9131</a>)</li>
<li><code>[@mantine/dates]</code> TimePicker: Fix incorrect focus handling of partially filled hours field (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dates/issues/9128">#9128</a>)</li>
<li><code>[@mantine/core]</code> RollingNumber: Fix incorrect copy event handling (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dates/issues/9132">#9132</a>)</li>
<li><code>[@mantine/core]</code> Notification: Fix incorrect <code>closeButtonProps</code> type (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dates/issues/9134">#9134</a>)</li>
<li><code>[@mantine/code-highlight]</code> Add support for lazy languages loading (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dates/issues/9141">#9141</a>)</li>
<li><code>[@mantine/code-highlight]</code> CodeHighlight: Add prop to keep indentation of the first line of the code block (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dates/issues/9140">#9140</a>)</li>
<li><code>[@mantine/dates]</code> Add missing formatting functions to MiniCalendarm DateInput and YarsList components</li>
<li><code>[@mantine/schedule]</code> WeekView: Improve performance of events positioning algorithm (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dates/issues/9075">#9075</a>)</li>
<li><code>[@mantine/form]</code> Add new useWatchValue hook</li>
<li><code>[@mantine/core]</code> Fix Combobox-based components not working correctly with Chrome autocomplete</li>
</ul>
<h2>9.5.1</h2>
<ul>
<li><code>[@mantine/tiptap]</code> Fix controls being initially disabledbefore element is focused</li>
<li><code>[@mantine/tiptap]</code> Fix source code control wrapping content with extra p tag</li>
<li><code>[@mantine/hooks]</code> use-scroll-spy: Allow usage with refs (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dates/issues/9025">#9025</a>)</li>
<li><code>[@mantine/core]</code> ColorInput: Add support for fullWidth prop (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dates/issues/9061">#9061</a>)</li>
<li><code>[@mantine/core]</code> Checkbox: Fix incottect indeterminate aria attributes handling in Checkbox.Card (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dates/issues/9095">#9095</a>)</li>
<li><code>[@mantine/core]</code> FloatingIndicator: Fix position and size calculation under scaled ancestors (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dates/issues/9071">#9071</a>)</li>
<li><code>[@mantine/core]</code> Tooltip: Add interactive prop support (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dates/issues/9072">#9072</a>)</li>
<li><code>[@mantine/core]</code> Cascader: Add safe area polygon support</li>
<li><code>[@mantine/core]</code> PasswordInput: Add option to change whether the visibility toggle is focusable (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dates/issues/9090">#9090</a>)</li>
<li><code>[@mantine/charts]</code> ScatterChart: Add option to add second y axis</li>
<li><code>[@mantine/schedule]</code> YearView: Add <code>renderDay</code> prop support</li>
<li><code>[@mantine/schedule]</code> YearView: Add option to hide weekend days</li>
<li><code>[@mantine/core]</code> InputWrapper: Fix <code>component: div</code> triggering typescript error if passed to <code>descriptionProps</code></li>
<li><code>[@mantine/schedule]</code> ResourcesMonthView: Add option to resize events</li>
<li><code>[@mantine/core]</code> FloatingWindow: Add support for  <code>onSizeChange</code> and <code>onResizeStart</code> props (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dates/issues/9085">#9085</a>)</li>
</ul>
<h2>9.5.0 🤖</h2>
<p><a href="https://mantine.dev/changelog/9-5-0">View changelog with demos on mantine.dev website</a></p>
<h2>Support Mantine development</h2>
<p>You can now sponsor Mantine development with <a href="https://opencollective.com/mantinedev">OpenCollective</a>.
All funds are used to improve Mantine and create new features and components.</p>
<h2>Migration to oxc</h2>
<p>Mantine has migrated its linting and formatting toolchain from ESLint and Prettier
to <a href="https://oxc.rs">oxc</a> – <a href="https://www.npmjs.com/package/oxlint">oxlint</a> is now used
as the linter and <a href="https://www.npmjs.com/package/oxfmt">oxfmt</a> as the formatter. Both
tools are written in Rust and are significantly faster than their predecessors, which
makes linting and formatting the entire codebase almost instant.</p>
<p>The shared configuration is available as a new
<a href="https://mantine.dev/oxc-config-mantine">oxc-config-mantine</a> package (a replacement for the previous</p>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/mantinedev/mantine/commit/8a284e2c2c53a9cb6f39f5dc389bf41b7a2073f8"><code>8a284e2</code></a> [release] Version: 9.5.2</li>
<li><a href="https://github.com/mantinedev/mantine/commit/38a41b273813f785b08983422283ca1f6d201c97"><code>38a41b2</code></a> [<code>@​mantine/dates</code>] Fix <code>minDate</code> used for default date in some cases (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dates/issues/9117">#9117</a>)</li>
<li><a href="https://github.com/mantinedev/mantine/commit/7f499c0c42e0a1533484644328eaf412b46a9a21"><code>7f499c0</code></a> [<code>@​mantine/dates</code>] TimePicker: Fix incorrect focus handling of partially filled...</li>
<li><a href="https://github.com/mantinedev/mantine/commit/6b82799329d41a10237c13e484369a035df334fd"><code>6b82799</code></a> [<code>@​mantine/dates</code>] Add missing formatting functions to MiniCalendarm DateInput ...</li>
<li><a href="https://github.com/mantinedev/mantine/commit/0f57eaf5ae90c9e870fbb2a4cdd61a1d58c4c01d"><code>0f57eaf</code></a> [release] Version: 9.5.1</li>
<li><a href="https://github.com/mantinedev/mantine/commit/ca9bc6f156b63f1a10918d94ec31ec18e4e60546"><code>ca9bc6f</code></a> [release] Version: 9.5.1-alpha.1</li>
<li><a href="https://github.com/mantinedev/mantine/commit/8f1ad1bbe545c9cafafc5aef5b059d3d48e676a6"><code>8f1ad1b</code></a> [release] Version: 9.5.1-alpha.0</li>
<li><a href="https://github.com/mantinedev/mantine/commit/f1d330613f54dc9319d176e6d8ba5ebff233da18"><code>f1d3306</code></a> [release] Version: 9.5.0</li>
<li><a href="https://github.com/mantinedev/mantine/commit/732056219a0283f5822001981d7f652e632c4c87"><code>7320562</code></a> [release] Version: 9.4.3</li>
<li><a href="https://github.com/mantinedev/mantine/commit/170c45a5feed2386a464a7f05ae3daf6379cea04"><code>170c45a</code></a> Merge branch '9.5'</li>
<li>Additional commits viewable in <a href="https://github.com/mantinedev/mantine/commits/9.5.2/packages/@mantine/dates">compare view</a></li>
</ul>
</details>
<details>
<summary>Maintainer changes</summary>
<p>This version was pushed to npm by <a href="https://www.npmjs.com/~GitHub%20Actions">GitHub Actions</a>, a new releaser for <code>@​mantine/dates</code> since your current version.</p>
</details>
<br />

Updates `@mantine/dropzone` from 9.1.1 to 9.5.2
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/mantinedev/mantine/releases">@​mantine/dropzone's releases</a>.</em></p>
<blockquote>
<h2>9.5.2</h2>
<ul>
<li><code>[@mantine/hooks]</code> use-debounced-value: Fix <code>leading: true</code> firing multiple times per burst and emiting a stale value (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dropzone/issues/9119">#9119</a>)</li>
<li><code>[@mantine/schedule]</code> Fix recurring events not working with timzones (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dropzone/issues/9112">#9112</a>)</li>
<li><code>[@mantine/dates]</code> Fix <code>minDate</code> used for default date in some cases (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dropzone/issues/9117">#9117</a>)</li>
<li><code>[@mantine/core]</code> Tooltip: Fix tooltip setting NaN in top/left position style when event position values cannot be read (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dropzone/issues/9131">#9131</a>)</li>
<li><code>[@mantine/dates]</code> TimePicker: Fix incorrect focus handling of partially filled hours field (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dropzone/issues/9128">#9128</a>)</li>
<li><code>[@mantine/core]</code> RollingNumber: Fix incorrect copy event handling (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dropzone/issues/9132">#9132</a>)</li>
<li><code>[@mantine/core]</code> Notification: Fix incorrect <code>closeButtonProps</code> type (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dropzone/issues/9134">#9134</a>)</li>
<li><code>[@mantine/code-highlight]</code> Add support for lazy languages loading (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dropzone/issues/9141">#9141</a>)</li>
<li><code>[@mantine/code-highlight]</code> CodeHighlight: Add prop to keep indentation of the first line of the code block (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dropzone/issues/9140">#9140</a>)</li>
<li><code>[@mantine/dates]</code> Add missing formatting functions to MiniCalendarm DateInput and YarsList components</li>
<li><code>[@mantine/schedule]</code> WeekView: Improve performance of events positioning algorithm (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dropzone/issues/9075">#9075</a>)</li>
<li><code>[@mantine/form]</code> Add new useWatchValue hook</li>
<li><code>[@mantine/core]</code> Fix Combobox-based components not working correctly with Chrome autocomplete</li>
</ul>
<h2>9.5.1</h2>
<ul>
<li><code>[@mantine/tiptap]</code> Fix controls being initially disabledbefore element is focused</li>
<li><code>[@mantine/tiptap]</code> Fix source code control wrapping content with extra p tag</li>
<li><code>[@mantine/hooks]</code> use-scroll-spy: Allow usage with refs (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dropzone/issues/9025">#9025</a>)</li>
<li><code>[@mantine/core]</code> ColorInput: Add support for fullWidth prop (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dropzone/issues/9061">#9061</a>)</li>
<li><code>[@mantine/core]</code> Checkbox: Fix incottect indeterminate aria attributes handling in Checkbox.Card (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dropzone/issues/9095">#9095</a>)</li>
<li><code>[@mantine/core]</code> FloatingIndicator: Fix position and size calculation under scaled ancestors (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dropzone/issues/9071">#9071</a>)</li>
<li><code>[@mantine/core]</code> Tooltip: Add interactive prop support (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dropzone/issues/9072">#9072</a>)</li>
<li><code>[@mantine/core]</code> Cascader: Add safe area polygon support</li>
<li><code>[@mantine/core]</code> PasswordInput: Add option to change whether the visibility toggle is focusable (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dropzone/issues/9090">#9090</a>)</li>
<li><code>[@mantine/charts]</code> ScatterChart: Add option to add second y axis</li>
<li><code>[@mantine/schedule]</code> YearView: Add <code>renderDay</code> prop support</li>
<li><code>[@mantine/schedule]</code> YearView: Add option to hide weekend days</li>
<li><code>[@mantine/core]</code> InputWrapper: Fix <code>component: div</code> triggering typescript error if passed to <code>descriptionProps</code></li>
<li><code>[@mantine/schedule]</code> ResourcesMonthView: Add option to resize events</li>
<li><code>[@mantine/core]</code> FloatingWindow: Add support for  <code>onSizeChange</code> and <code>onResizeStart</code> props (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dropzone/issues/9085">#9085</a>)</li>
</ul>
<h2>9.5.0 🤖</h2>
<p><a href="https://mantine.dev/changelog/9-5-0">View changelog with demos on mantine.dev website</a></p>
<h2>Support Mantine development</h2>
<p>You can now sponsor Mantine development with <a href="https://opencollective.com/mantinedev">OpenCollective</a>.
All funds are used to improve Mantine and create new features and components.</p>
<h2>Migration to oxc</h2>
<p>Mantine has migrated its linting and formatting toolchain from ESLint and Prettier
to <a href="https://oxc.rs">oxc</a> – <a href="https://www.npmjs.com/package/oxlint">oxlint</a> is now used
as the linter and <a href="https://www.npmjs.com/package/oxfmt">oxfmt</a> as the formatter. Both
tools are written in Rust and are significantly faster than their predecessors, which
makes linting and formatting the entire codebase almost instant.</p>
<p>The shared configuration is available as a new
<a href="https://mantine.dev/oxc-config-mantine">oxc-config-mantine</a> package (a replacement for the previous</p>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/mantinedev/mantine/commit/8a284e2c2c53a9cb6f39f5dc389bf41b7a2073f8"><code>8a284e2</code></a> [release] Version: 9.5.2</li>
<li><a href="https://github.com/mantinedev/mantine/commit/0f57eaf5ae90c9e870fbb2a4cdd61a1d58c4c01d"><code>0f57eaf</code></a> [release] Version: 9.5.1</li>
<li><a href="https://github.com/mantinedev/mantine/commit/ca9bc6f156b63f1a10918d94ec31ec18e4e60546"><code>ca9bc6f</code></a> [release] Version: 9.5.1-alpha.1</li>
<li><a href="https://github.com/mantinedev/mantine/commit/8f1ad1bbe545c9cafafc5aef5b059d3d48e676a6"><code>8f1ad1b</code></a> [release] Version: 9.5.1-alpha.0</li>
<li><a href="https://github.com/mantinedev/mantine/commit/f1d330613f54dc9319d176e6d8ba5ebff233da18"><code>f1d3306</code></a> [release] Version: 9.5.0</li>
<li><a href="https://github.com/mantinedev/mantine/commit/732056219a0283f5822001981d7f652e632c4c87"><code>7320562</code></a> [release] Version: 9.4.3</li>
<li><a href="https://github.com/mantinedev/mantine/commit/de21a8203060ba29441ab7623244339748e4319d"><code>de21a82</code></a> [release] Version: 9.4.3-alpha.0</li>
<li><a href="https://github.com/mantinedev/mantine/commit/e5752de4067bd58f6cdd970660b3c8469a56d4e5"><code>e5752de</code></a> [release] Version: 9.4.2</li>
<li><a href="https://github.com/mantinedev/mantine/commit/d709e0bc277255c2a857f138cc694028273d8697"><code>d709e0b</code></a> [release] Version: 9.4.1</li>
<li><a href="https://github.com/mantinedev/mantine/commit/75d5ab5b419f3aa560bb56fc1d75d7815c5fb2f8"><code>75d5ab5</code></a> [release] Version: 9.4.0</li>
<li>Additional commits viewable in <a href="https://github.com/mantinedev/mantine/commits/9.5.2/packages/@mantine/dropzone">compare view</a></li>
</ul>
</details>
<details>
<summary>Maintainer changes</summary>
<p>This version was pushed to npm by <a href="https://www.npmjs.com/~GitHub%20Actions">GitHub Actions</a>, a new releaser for <code>@​mantine/dropzone</code> since your current version.</p>
</details>
<br />

Updates `@mantine/form` from 9.1.1 to 9.5.2
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/mantinedev/mantine/releases">@​mantine/form's releases</a>.</em></p>
<blockquote>
<h2>9.5.2</h2>
<ul>
<li><code>[@mantine/hooks]</code> use-debounced-value: Fix <code>leading: true</code> firing multiple times per burst and emiting a stale value (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/form/issues/9119">#9119</a>)</li>
<li><code>[@mantine/schedule]</code> Fix recurring events not working with timzones (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/form/issues/9112">#9112</a>)</li>
<li><code>[@mantine/dates]</code> Fix <code>minDate</code> used for default date in some cases (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/form/issues/9117">#9117</a>)</li>
<li><code>[@mantine/core]</code> Tooltip: Fix tooltip setting NaN in top/left position style when event position values cannot be read (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/form/issues/9131">#9131</a>)</li>
<li><code>[@mantine/dates]</code> TimePicker: Fix incorrect focus handling of partially filled hours field (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/form/issues/9128">#9128</a>)</li>
<li><code>[@mantine/core]</code> RollingNumber: Fix incorrect copy event handling (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/form/issues/9132">#9132</a>)</li>
<li><code>[@mantine/core]</code> Notification: Fix incorrect <code>closeButtonProps</code> type (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/form/issues/9134">#9134</a>)</li>
<li><code>[@mantine/code-highlight]</code> Add support for lazy languages loading (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/form/issues/9141">#9141</a>)</li>
<li><code>[@mantine/code-highlight]</code> CodeHighlight: Add prop to keep indentation of the first line of the code block (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/form/issues/9140">#9140</a>)</li>
<li><code>[@mantine/dates]</code> Add missing formatting functions to MiniCalendarm DateInput and YarsList components</li>
<li><code>[@mantine/schedule]</code> WeekView: Improve performance of events positioning algorithm (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/form/issues/9075">#9075</a>)</li>
<li><code>[@mantine/form]</code> Add new useWatchValue hook</li>
<li><code>[@mantine/core]</code> Fix Combobox-based components not working correctly with Chrome autocomplete</li>
</ul>
<h2>9.5.1</h2>
<ul>
<li><code>[@mantine/tiptap]</code> Fix controls being initially disabledbefore element is focused</li>
<li><code>[@mantine/tiptap]</code> Fix source code control wrapping content with extra p tag</li>
<li><code>[@mantine/hooks]</code> use-scroll-spy: Allow usage with refs (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/form/issues/9025">#9025</a>)</li>
<li><code>[@mantine/core]</code> ColorInput: Add support for fullWidth prop (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/form/issues/9061">#9061</a>)</li>
<li><code>[@mantine/core]</code> Checkbox: Fix incottect indeterminate aria attributes handling in Checkbox.Card (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/form/issues/9095">#9095</a>)</li>
<li><code>[@mantine/core]</code> FloatingIndicator: Fix position and size calculation under scaled ancestors (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/form/issues/9071">#9071</a>)</li>
<li><code>[@mantine/core]</code> Tooltip: Add interactive prop support (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/form/issues/9072">#9072</a>)</li>
<li><code>[@mantine/core]</code> Cascader: Add safe area polygon support</li>
<li><code>[@mantine/core]</code> PasswordInput: Add option to change whether the visibility toggle is focusable (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/form/issues/9090">#9090</a>)</li>
<li><code>[@mantine/charts]</code> ScatterChart: Add option to add second y axis</li>
<li><code>[@mantine/schedule]</code> YearView: Add <code>renderDay</code> prop support</li>
<li><code>[@mantine/schedule]</code> YearView: Add option to hide weekend days</li>
<li><code>[@mantine/core]</code> InputWrapper: Fix <code>component: div</code> triggering typescript error if passed to <code>descriptionProps</code></li>
<li><code>[@mantine/schedule]</code> ResourcesMonthView: Add option to resize events</li>
<li><code>[@mantine/core]</code> FloatingWindow: Add support for  <code>onSizeChange</code> and <code>onResizeStart</code> props (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/form/issues/9085">#9085</a>)</li>
</ul>
<h2>9.5.0 🤖</h2>
<p><a href="https://mantine.dev/changelog/9-5-0">View changelog with demos on mantine.dev website</a></p>
<h2>Support Mantine development</h2>
<p>You can now sponsor Mantine development with <a href="https://opencollective.com/mantinedev">OpenCollective</a>.
All funds are used to improve Mantine and create new features and components.</p>
<h2>Migration to oxc</h2>
<p>Mantine has migrated its linting and formatting toolchain from ESLint and Prettier
to <a href="https://oxc.rs">oxc</a> – <a href="https://www.npmjs.com/package/oxlint">oxlint</a> is now used
as the linter and <a href="https://www.npmjs.com/package/oxfmt">oxfmt</a> as the formatter. Both
tools are written in Rust and are significantly faster than their predecessors, which
makes linting and formatting the entire codebase almost instant.</p>
<p>The shared configuration is available as a new
<a href="https://mantine.dev/oxc-config-mantine">oxc-config-mantine</a> package (a replacement for the previous</p>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/mantinedev/mantine/commit/8a284e2c2c53a9cb6f39f5dc389bf41b7a2073f8"><code>8a284e2</code></a> [release] Version: 9.5.2</li>
<li><a href="https://github.com/mantinedev/mantine/commit/698381d31dee39f3d6f5ac58df7d6968eee01cb8"><code>698381d</code></a> [<code>@​mantine/form</code>] Add new useWatchValue hook</li>
<li><a href="https://github.com/mantinedev/mantine/commit/0f57eaf5ae90c9e870fbb2a4cdd61a1d58c4c01d"><code>0f57eaf</code></a> [release] Version: 9.5.1</li>
<li><a href="https://github.com/mantinedev/mantine/commit/ca9bc6f156b63f1a10918d94ec31ec18e4e60546"><code>ca9bc6f</code></a> [release] Version: 9.5.1-alpha.1</li>
<li><a href="https://github.com/mantinedev/mantine/commit/8f1ad1bbe545c9cafafc5aef5b059d3d48e676a6"><code>8f1ad1b</code></a> [release] Version: 9.5.1-alpha.0</li>
<li><a href="https://github.com/mantinedev/mantine/commit/f1d330613f54dc9319d176e6d8ba5ebff233da18"><code>f1d3306</code></a> [release] Version: 9.5.0</li>
<li><a href="https://github.com/mantinedev/mantine/commit/732056219a0283f5822001981d7f652e632c4c87"><code>7320562</code></a> [release] Version: 9.4.3</li>
<li><a href="https://github.com/mantinedev/mantine/commit/de21a8203060ba29441ab7623244339748e4319d"><code>de21a82</code></a> [release] Version: 9.4.3-alpha.0</li>
<li><a href="https://github.com/mantinedev/mantine/commit/e5752de4067bd58f6cdd970660b3c8469a56d4e5"><code>e5752de</code></a> [release] Version: 9.4.2</li>
<li><a href="https://github.com/mantinedev/mantine/commit/1d68be7025ceca3619eda9db00b9395c53b75c0a"><code>1d68be7</code></a> [<code>@​mantine/form</code>] Fix async validation with debounce on initial keystroke of em...</li>
<li>Additional commits viewable in <a href="https://github.com/mantinedev/mantine/commits/9.5.2/packages/@mantine/form">compare view</a></li>
</ul>
</details>
<details>
<summary>Maintainer changes</summary>
<p>This version was pushed to npm by <a href="https://www.npmjs.com/~GitHub%20Actions">GitHub Actions</a>, a new releaser for <code>@​mantine/form</code> since your current version.</p>
</details>
<br />

Updates `@mantine/hooks` from 9.1.1 to 9.5.2
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/mantinedev/mantine/releases">@​mantine/hooks's releases</a>.</em></p>
<blockquote>
<h2>9.5.2</h2>
<ul>
<li><code>[@mantine/hooks]</code> use-debounced-value: Fix <code>leading: true</code> firing multiple times per burst and emiting a stale value (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/hooks/issues/9119">#9119</a>)</li>
<li><code>[@mantine/schedule]</code> Fix recurring events not working with timzones (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/hooks/issues/9112">#9112</a>)</li>
<li><code>[@mantine/dates]</code> Fix <code>minDate</code> used for default date in some cases (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/hooks/issues/9117">#9117</a>)</li>
<li><code>[@mantine/core]</code> Tooltip: Fix tooltip setting NaN in top/left position style when event position values cannot be read (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/hooks/issues/9131">#9131</a>)</li>
<li><code>[@mantine/dates]</code> TimePicker: Fix incorrect focus handling of partially filled hours field (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/hooks/issues/9128">#9128</a>)</li>
<li><code>[@mantine/core]</code> RollingNumber: Fix incorrect copy event handling (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/hooks/issues/9132">#9132</a>)</li>
<li><code>[@mantine/core]</code> Notification: Fix incorrect <code>closeButtonProps</code> type (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/hooks/issues/9134">#9134</a>)</li>
<li><code>[@mantine/code-highlight]</code> Add support for lazy languages loading (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/hooks/issues/9141">#9141</a>)</li>
<li><code>[@mantine/code-highlight]</code> CodeHighlight: Add prop to keep indentation of the first line of the code block (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/hooks/issues/9140">#9140</a>)</li>
<li><code>[@mantine/dates]</code> Add missing formatting functions to MiniCalendarm DateInput and YarsList components</li>
<li><code>[@mantine/schedule]</code> WeekView: Improve performance of events positioning algorithm (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/hooks/issues/9075">#9075</a>)</li>
<li><code>[@mantine/form]</code> Add new useWatchValue hook</li>
<li><code>[@mantine/core]</code> Fix Combobox-based components not working correctly with Chrome autocomplete</li>
</ul>
<h2>9.5.1</h2>
<ul>
<li><code>[@mantine/tiptap]</code> Fix controls being initially disabledbefore element is focused</li>
<li><code>[@mantine/tiptap]</code> Fix source code control wrapping content with extra p tag</li>
<li><code>[@mantine/hooks]</code> use-scroll-spy: Allow usage with refs (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/hooks/issues/9025">#9025</a>)</li>
<li><code>[@mantine/core]</code> ColorInput: Add support for fullWidth prop (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/hooks/issues/9061">#9061</a>)</li>
<li><code>[@mantine/core]</code> Checkbox: Fix incottect indeterminate aria attributes handling in Checkbox.Card (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/hooks/issues/9095">#9095</a>)</li>
<li><code>[@mantine/core]</code> FloatingIndicator: Fix position and size calculation under scaled ancestors (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/hooks/issues/9071">#9071</a>)</li>
<li><code>[@mantine/core]</code> Tooltip: Add interactive prop support (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/hooks/issues/9072">#9072</a>)</li>
<li><code>[@mantine/core]</code> Cascader: Add safe area polygon support</li>
<li><code>[@mantine/core]</code> PasswordInput: Add option to change whether the visibility toggle is focusable (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/hooks/issues/9090">#9090</a>)</li>
<li><code>[@mantine/charts]</code> ScatterChart: Add option to add second y axis</li>
<li><code>[@mantine/schedule]</code> YearView: Add <code>renderDay</code> prop support</li>
<li><code>[@mantine/schedule]</code> YearView: Add option to hide weekend days</li>
<li><code>[@mantine/core]</code> InputWrapper: Fix <code>component: div</code> triggering typescript error if passed to <code>descriptionProps</code></li>
<li><code>[@mantine/schedule]</code> ResourcesMonthView: Add option to resize events</li>
<li><code>[@mantine/core]</code> FloatingWindow: Add support for  <code>onSizeChange</code> and <code>onResizeStart</code> props (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/hooks/issues/9085">#9085</a>)</li>
</ul>
<h2>9.5.0 🤖</h2>
<p><a href="https://mantine.dev/changelog/9-5-0">View changelog with demos on mantine.dev website</a></p>
<h2>Support Mantine development</h2>
<p>You can now sponsor Mantine development with <a href="https://opencollective.com/mantinedev">OpenCollective</a>.
All funds are used to improve Mantine and create new features and components.</p>
<h2>Migration to oxc</h2>
<p>Mantine has migrated its linting and formatting toolchain from ESLint and Prettier
to <a href="https://oxc.rs">oxc</a> – <a href="https://www.npmjs.com/package/oxlint">oxlint</a> is now used
as the linter and <a href="https://www.npmjs.com/package/oxfmt">oxfmt</a> as the formatter. Both
tools are written in Rust and are significantly faster than their predecessors, which
makes linting and formatting the entire codebase almost instant.</p>
<p>The shared configuration is available as a new
<a href="https://mantine.dev/oxc-config-mantine">oxc-config-mantine</a> package (a replacement for the previous</p>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/mantinedev/mantine/commit/8a284e2c2c53a9cb6f39f5dc389bf41b7a2073f8"><code>8a284e2</code></a> [release] Version: 9.5.2</li>
<li><a href="https://github.com/mantinedev/mantine/commit/1f93ee57929e7f100bad1d3308c6d0f4f8a6d1ed"><code>1f93ee5</code></a> [<code>@​mantine/hooks</code>] use-debounced-value: Fix <code>leading: true</code> firing multiple tim...</li>
<li><a href="https://github.com/mantinedev/mantine/commit/0f57eaf5ae90c9e870fbb2a4cdd61a1d58c4c01d"><code>0f57eaf</code></a> [release] Version: 9.5.1</li>
<li><a href="https://github.com/mantinedev/mantine/commit/ce00cdde77a5ff69d20ad8f29956cd898c50d619"><code>ce00cdd</code></a> [<code>@​mantine/hooks</code>] use-scroll-spy: Allow usage with refs (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/hooks/issues/9025">#9025</a>)</li>
<li><a href="https://github.com/mantinedev/mantine/commit/ca9bc6f156b63f1a10918d94ec31ec18e4e60546"><code>ca9bc6f</code></a> [release] Version: 9.5.1-alpha.1</li>
<li><a href="https://github.com/mantinedev/mantine/commit/8f1ad1bbe545c9cafafc5aef5b059d3d48e676a6"><code>8f1ad1b</code></a> [release] Version: 9.5.1-alpha.0</li>
<li><a href="https://github.com/mantinedev/mantine/commit/953192e2207722a43568468d0db8d20eceb21307"><code>953192e</code></a> [<code>@​mantine/core</code>] FloatingWindow: Add support for  <code>onSizeChange</code> and `onResize...</li>
<li><a href="https://github.com/mantinedev/mantine/commit/f1d330613f54dc9319d176e6d8ba5ebff233da18"><code>f1d3306</code></a> [release] Version: 9.5.0</li>
<li><a href="https://github.com/mantinedev/mantine/commit/732056219a0283f5822001981d7f652e632c4c87"><code>7320562</code></a> [release] Version: 9.4.3</li>
<li><a href="https://github.com/mantinedev/mantine/commit/de21a8203060ba29441ab7623244339748e4319d"><code>de21a82</code></a> [release] Version: 9.4.3-alpha.0</li>
<li>Additional commits viewable in <a href="https://github.com/mantinedev/mantine/commits/9.5.2/packages/@mantine/hooks">compare view</a></li>
</ul>
</details>
<details>
<summary>Maintainer changes</summary>
<p>This version was pushed to npm by <a href="https://www.npmjs.com/~GitHub%20Actions">GitHub Actions</a>, a new releaser for <code>@​mantine/hooks</code> since your current version.</p>
</details>
<br />

Updates `@mantine/modals` from 9.1.1 to 9.5.2
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/mantinedev/mantine/releases">@​mantine/modals's releases</a>.</em></p>
<blockquote>
<h2>9.5.2</h2>
<ul>
<li><code>[@mantine/hooks]</code> use-debounced-value: Fix <code>leading: true</code> firing multiple times per burst and emiting a stale value (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/modals/issues/9119">#9119</a>)</li>
<li><code>[@mantine/schedule]</code> Fix recurring events not working with timzones (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/modals/issues/9112">#9112</a>)</li>
<li><code>[@mantine/dates]</code> Fix <code>minDate</code> used for default date in some cases (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/modals/issues/9117">#9117</a>)</li>
<li><code>[@mantine/core]</code> Tooltip: Fix tooltip setting NaN in top/left position style when event position values cannot be read (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/modals/issues/9131">#9131</a>)</li>
<li><code>[@mantine/dates]</code> TimePicker: Fix incorrect focus handling of partially filled hours field (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/modals/issues/9128">#9128</a>)</li>
<li><code>[@mantine/core]</code> RollingNumber: Fix incorrect copy event handling (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/modals/issues/9132">#9132</a>)</li>
<li><code>[@mantine/core]</code> Notification: Fix incorrect <code>closeButtonProps</code> type (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/modals/issues/9134">#9134</a>)</li>
<li><code>[@mantine/code-highlight]</code> Add support for lazy languages loading (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/modals/issues/9141">#9141</a>)</li>
<li><code>[@mantine/code-highlight]</code> CodeHighlight: Add prop to keep indentation of the first line of the code block (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/modals/issues/9140">#914...

_Description has been truncated_

<details><summary>Comment — nathanpond, 2026-08-31</summary>

@dependabot rebase

</details>

<details><summary>Comment — dependabot[bot], 2026-08-31</summary>

Looks like these dependencies are updatable in another way, so this is no longer needed.

</details>

---

## archived-156 — Hocuspocus 4.0 → 4.6: server and provider together

`MERGED (merged 2026-08-31)` · nathanpond · opened 2026-08-31 · `feat/hocuspocus-4.6` → `master`

Closes archived-154

## What
`@hocuspocus/server` 4.6.0 in `services/hocuspocus` (plus pg 8.23, `@types/pg`, yjs 13.6.32, react 19.2.8 — the rest of Dependabot archived-104 now that BlockNote is at 0.54) and `@hocuspocus/provider` 4.6.0 + yjs 13.6.32 in the SPA, in one change so the two ends never disagree.

## Verification
- [x] hocuspocus `tsc`; SPA `tsc` / lint / Vite build; single `yjs` 13.6.32 in each tree.
- [x] Full Playwright suite against the rebuilt sidecar (server 4.6.0): **140 passed / 0 failed / 2 skipped**.
- [x] Manual sync round-trip on a throwaway page in the dev DB: text typed in the editor → hocuspocus 4.6 → `onStoreDocument` → materializer → `/internal/yjs-webhook` → persisted `bodyJsonb` (page version 3 contains the text); two Yjs tickets issued, zero console errors. Page deleted afterwards.

## Notes
- The E2E suite's notes/documents specs pass even when the sidecar can't authenticate (the fixture app runs on a random port; the compose sidecar calls `:5108`), i.e. they exercise the local Y.Doc, not sync. Worth a follow-up spec that asserts the webhook-persisted body.
- Dependabot archived-104 is superseded by this PR.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Y5ie3qTEptr4MjYw5i6a5F

---

## archived-157 — chore(deps): bump the spa-minor-patch group across 1 directory with 28 updates

`MERGED (merged 2026-08-31)` · app/dependabot · opened 2026-08-31 · `dependabot/npm_and_yarn/src/AutoNate.Spa/spa-minor-patch-5a2278a040` → `master`

Bumps the spa-minor-patch group with 28 updates in the /src/AutoNate.Spa directory:

| Package | From | To |
| --- | --- | --- |
| [@codemirror/lang-html](https://github.com/codemirror/lang-html) | `6.4.11` | `6.4.12` |
| [@fortawesome/fontawesome-free](https://github.com/FortAwesome/Font-Awesome) | `7.2.0` | `7.3.1` |
| [@mantine/charts](https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts) | `9.1.1` | `9.5.2` |
| [@mantine/colors-generator](https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator) | `9.1.1` | `9.5.2` |
| [@mantine/core](https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/core) | `9.1.1` | `9.5.2` |
| [@mantine/dates](https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dates) | `9.1.1` | `9.5.2` |
| [@mantine/dropzone](https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dropzone) | `9.1.1` | `9.5.2` |
| [@mantine/form](https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/form) | `9.1.1` | `9.5.2` |
| [@mantine/hooks](https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/hooks) | `9.1.1` | `9.5.2` |
| [@mantine/modals](https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/modals) | `9.1.1` | `9.5.2` |
| [@mantine/notifications](https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/notifications) | `9.1.1` | `9.5.2` |
| [@tanstack/react-query](https://github.com/TanStack/query/tree/HEAD/packages/react-query) | `5.100.1` | `5.102.8` |
| [@tanstack/react-query-devtools](https://github.com/TanStack/query/tree/HEAD/packages/react-query-devtools) | `5.100.1` | `5.102.8` |
| [@uiw/react-codemirror](https://github.com/uiwjs/react-codemirror) | `4.25.9` | `4.25.11` |
| [@xyflow/react](https://github.com/xyflow/xyflow/tree/HEAD/packages/react) | `12.10.2` | `12.11.5` |
| [axios](https://github.com/axios/axios) | `1.18.0` | `1.20.0` |
| [marked](https://github.com/markedjs/marked) | `18.0.4` | `18.0.11` |
| [react](https://github.com/react/react/tree/HEAD/packages/react) | `19.2.5` | `19.2.8` |
| [@types/react](https://github.com/DefinitelyTyped/DefinitelyTyped/tree/HEAD/types/react) | `19.2.14` | `19.2.18` |
| [react-dom](https://github.com/react/react/tree/HEAD/packages/react-dom) | `19.2.5` | `19.2.8` |
| [@types/react-dom](https://github.com/DefinitelyTyped/DefinitelyTyped/tree/HEAD/types/react-dom) | `19.2.3` | `19.2.5` |
| [react-grid-layout](https://github.com/STRML/react-grid-layout) | `2.2.3` | `2.2.4` |
| [@types/react-grid-layout](https://github.com/DefinitelyTyped/DefinitelyTyped/tree/HEAD/types/react-grid-layout) | `1.3.6` | `2.1.0` |
| [recharts](https://github.com/recharts/recharts) | `3.8.1` | `3.10.1` |
| [zod](https://github.com/colinhacks/zod) | `4.3.6` | `4.4.3` |
| [@vitejs/plugin-react](https://github.com/vitejs/vite-plugin-react/tree/HEAD/packages/plugin-react) | `6.0.1` | `6.1.1` |
| [globals](https://github.com/sindresorhus/globals) | `17.6.0` | `17.11.0` |
| [typescript-eslint](https://github.com/typescript-eslint/typescript-eslint/tree/HEAD/packages/typescript-eslint) | `8.60.0` | `8.68.0` |


Updates `@codemirror/lang-html` from 6.4.11 to 6.4.12
<details>
<summary>Commits</summary>
<ul>
<li>See full diff in <a href="https://github.com/codemirror/lang-html/commits">compare view</a></li>
</ul>
</details>
<br />

Updates `@fortawesome/fontawesome-free` from 7.2.0 to 7.3.1
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/FortAwesome/Font-Awesome/releases">@​fortawesome/fontawesome-free's releases</a>.</em></p>
<blockquote>
<h2>Release 7.3.1</h2>
<p><strong>Change log available at <a href="https://fontawesome.com/docs/changelog/">https://fontawesome.com/docs/changelog/</a></strong></p>
<h2>Release 7.3.0</h2>
<p><strong>Change log available at <a href="https://fontawesome.com/docs/changelog/">https://fontawesome.com/docs/changelog/</a></strong></p>
</blockquote>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/FortAwesome/Font-Awesome/commit/14c65a3747d0f3b751f15831fc719236aea8729d"><code>14c65a3</code></a> Release 7.3.1 (<a href="https://redirect.github.com/FortAwesome/Font-Awesome/issues/21630">#21630</a>)</li>
<li><a href="https://github.com/FortAwesome/Font-Awesome/commit/70fb2dd154b617f62fc4ae5b0b7e2943bfd2aa96"><code>70fb2dd</code></a> Release 7.3.0 (<a href="https://redirect.github.com/FortAwesome/Font-Awesome/issues/21612">#21612</a>)</li>
<li>See full diff in <a href="https://github.com/FortAwesome/Font-Awesome/compare/7.2.0...7.3.1">compare view</a></li>
</ul>
</details>
<details>
<summary>Maintainer changes</summary>
<p>This version was pushed to npm by <a href="https://www.npmjs.com/~fortawesome-admin">fortawesome-admin</a>, a new releaser for <code>@​fortawesome/fontawesome-free</code> since your current version.</p>
</details>
<br />

Updates `@mantine/charts` from 9.1.1 to 9.5.2
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/mantinedev/mantine/releases">@​mantine/charts's releases</a>.</em></p>
<blockquote>
<h2>9.5.2</h2>
<ul>
<li><code>[@mantine/hooks]</code> use-debounced-value: Fix <code>leading: true</code> firing multiple times per burst and emiting a stale value (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9119">#9119</a>)</li>
<li><code>[@mantine/schedule]</code> Fix recurring events not working with timzones (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9112">#9112</a>)</li>
<li><code>[@mantine/dates]</code> Fix <code>minDate</code> used for default date in some cases (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9117">#9117</a>)</li>
<li><code>[@mantine/core]</code> Tooltip: Fix tooltip setting NaN in top/left position style when event position values cannot be read (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9131">#9131</a>)</li>
<li><code>[@mantine/dates]</code> TimePicker: Fix incorrect focus handling of partially filled hours field (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9128">#9128</a>)</li>
<li><code>[@mantine/core]</code> RollingNumber: Fix incorrect copy event handling (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9132">#9132</a>)</li>
<li><code>[@mantine/core]</code> Notification: Fix incorrect <code>closeButtonProps</code> type (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9134">#9134</a>)</li>
<li><code>[@mantine/code-highlight]</code> Add support for lazy languages loading (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9141">#9141</a>)</li>
<li><code>[@mantine/code-highlight]</code> CodeHighlight: Add prop to keep indentation of the first line of the code block (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9140">#9140</a>)</li>
<li><code>[@mantine/dates]</code> Add missing formatting functions to MiniCalendarm DateInput and YarsList components</li>
<li><code>[@mantine/schedule]</code> WeekView: Improve performance of events positioning algorithm (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9075">#9075</a>)</li>
<li><code>[@mantine/form]</code> Add new useWatchValue hook</li>
<li><code>[@mantine/core]</code> Fix Combobox-based components not working correctly with Chrome autocomplete</li>
</ul>
<h2>9.5.1</h2>
<ul>
<li><code>[@mantine/tiptap]</code> Fix controls being initially disabledbefore element is focused</li>
<li><code>[@mantine/tiptap]</code> Fix source code control wrapping content with extra p tag</li>
<li><code>[@mantine/hooks]</code> use-scroll-spy: Allow usage with refs (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9025">#9025</a>)</li>
<li><code>[@mantine/core]</code> ColorInput: Add support for fullWidth prop (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9061">#9061</a>)</li>
<li><code>[@mantine/core]</code> Checkbox: Fix incottect indeterminate aria attributes handling in Checkbox.Card (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9095">#9095</a>)</li>
<li><code>[@mantine/core]</code> FloatingIndicator: Fix position and size calculation under scaled ancestors (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9071">#9071</a>)</li>
<li><code>[@mantine/core]</code> Tooltip: Add interactive prop support (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9072">#9072</a>)</li>
<li><code>[@mantine/core]</code> Cascader: Add safe area polygon support</li>
<li><code>[@mantine/core]</code> PasswordInput: Add option to change whether the visibility toggle is focusable (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9090">#9090</a>)</li>
<li><code>[@mantine/charts]</code> ScatterChart: Add option to add second y axis</li>
<li><code>[@mantine/schedule]</code> YearView: Add <code>renderDay</code> prop support</li>
<li><code>[@mantine/schedule]</code> YearView: Add option to hide weekend days</li>
<li><code>[@mantine/core]</code> InputWrapper: Fix <code>component: div</code> triggering typescript error if passed to <code>descriptionProps</code></li>
<li><code>[@mantine/schedule]</code> ResourcesMonthView: Add option to resize events</li>
<li><code>[@mantine/core]</code> FloatingWindow: Add support for  <code>onSizeChange</code> and <code>onResizeStart</code> props (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/charts/issues/9085">#9085</a>)</li>
</ul>
<h2>9.5.0 🤖</h2>
<p><a href="https://mantine.dev/changelog/9-5-0">View changelog with demos on mantine.dev website</a></p>
<h2>Support Mantine development</h2>
<p>You can now sponsor Mantine development with <a href="https://opencollective.com/mantinedev">OpenCollective</a>.
All funds are used to improve Mantine and create new features and components.</p>
<h2>Migration to oxc</h2>
<p>Mantine has migrated its linting and formatting toolchain from ESLint and Prettier
to <a href="https://oxc.rs">oxc</a> – <a href="https://www.npmjs.com/package/oxlint">oxlint</a> is now used
as the linter and <a href="https://www.npmjs.com/package/oxfmt">oxfmt</a> as the formatter. Both
tools are written in Rust and are significantly faster than their predecessors, which
makes linting and formatting the entire codebase almost instant.</p>
<p>The shared configuration is available as a new
<a href="https://mantine.dev/oxc-config-mantine">oxc-config-mantine</a> package (a replacement for the previous</p>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/mantinedev/mantine/commit/8a284e2c2c53a9cb6f39f5dc389bf41b7a2073f8"><code>8a284e2</code></a> [release] Version: 9.5.2</li>
<li><a href="https://github.com/mantinedev/mantine/commit/0f57eaf5ae90c9e870fbb2a4cdd61a1d58c4c01d"><code>0f57eaf</code></a> [release] Version: 9.5.1</li>
<li><a href="https://github.com/mantinedev/mantine/commit/1e120595fdde5a414616df908bb3e600021d092e"><code>1e12059</code></a> [<code>@​mantine/charts</code>] ScatterChart: Add option to add second y axis</li>
<li><a href="https://github.com/mantinedev/mantine/commit/ca9bc6f156b63f1a10918d94ec31ec18e4e60546"><code>ca9bc6f</code></a> [release] Version: 9.5.1-alpha.1</li>
<li><a href="https://github.com/mantinedev/mantine/commit/8f1ad1bbe545c9cafafc5aef5b059d3d48e676a6"><code>8f1ad1b</code></a> [release] Version: 9.5.1-alpha.0</li>
<li><a href="https://github.com/mantinedev/mantine/commit/f1d330613f54dc9319d176e6d8ba5ebff233da18"><code>f1d3306</code></a> [release] Version: 9.5.0</li>
<li><a href="https://github.com/mantinedev/mantine/commit/732056219a0283f5822001981d7f652e632c4c87"><code>7320562</code></a> [release] Version: 9.4.3</li>
<li><a href="https://github.com/mantinedev/mantine/commit/170c45a5feed2386a464a7f05ae3daf6379cea04"><code>170c45a</code></a> Merge branch '9.5'</li>
<li><a href="https://github.com/mantinedev/mantine/commit/de21a8203060ba29441ab7623244339748e4319d"><code>de21a82</code></a> [release] Version: 9.4.3-alpha.0</li>
<li><a href="https://github.com/mantinedev/mantine/commit/e5752de4067bd58f6cdd970660b3c8469a56d4e5"><code>e5752de</code></a> [release] Version: 9.4.2</li>
<li>Additional commits viewable in <a href="https://github.com/mantinedev/mantine/commits/9.5.2/packages/@mantine/charts">compare view</a></li>
</ul>
</details>
<details>
<summary>Maintainer changes</summary>
<p>This version was pushed to npm by <a href="https://www.npmjs.com/~GitHub%20Actions">GitHub Actions</a>, a new releaser for <code>@​mantine/charts</code> since your current version.</p>
</details>
<br />

Updates `@mantine/colors-generator` from 9.1.1 to 9.5.2
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/mantinedev/mantine/releases">@​mantine/colors-generator's releases</a>.</em></p>
<blockquote>
<h2>9.5.2</h2>
<ul>
<li><code>[@mantine/hooks]</code> use-debounced-value: Fix <code>leading: true</code> firing multiple times per burst and emiting a stale value (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9119">#9119</a>)</li>
<li><code>[@mantine/schedule]</code> Fix recurring events not working with timzones (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9112">#9112</a>)</li>
<li><code>[@mantine/dates]</code> Fix <code>minDate</code> used for default date in some cases (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9117">#9117</a>)</li>
<li><code>[@mantine/core]</code> Tooltip: Fix tooltip setting NaN in top/left position style when event position values cannot be read (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9131">#9131</a>)</li>
<li><code>[@mantine/dates]</code> TimePicker: Fix incorrect focus handling of partially filled hours field (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9128">#9128</a>)</li>
<li><code>[@mantine/core]</code> RollingNumber: Fix incorrect copy event handling (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9132">#9132</a>)</li>
<li><code>[@mantine/core]</code> Notification: Fix incorrect <code>closeButtonProps</code> type (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9134">#9134</a>)</li>
<li><code>[@mantine/code-highlight]</code> Add support for lazy languages loading (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9141">#9141</a>)</li>
<li><code>[@mantine/code-highlight]</code> CodeHighlight: Add prop to keep indentation of the first line of the code block (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9140">#9140</a>)</li>
<li><code>[@mantine/dates]</code> Add missing formatting functions to MiniCalendarm DateInput and YarsList components</li>
<li><code>[@mantine/schedule]</code> WeekView: Improve performance of events positioning algorithm (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9075">#9075</a>)</li>
<li><code>[@mantine/form]</code> Add new useWatchValue hook</li>
<li><code>[@mantine/core]</code> Fix Combobox-based components not working correctly with Chrome autocomplete</li>
</ul>
<h2>9.5.1</h2>
<ul>
<li><code>[@mantine/tiptap]</code> Fix controls being initially disabledbefore element is focused</li>
<li><code>[@mantine/tiptap]</code> Fix source code control wrapping content with extra p tag</li>
<li><code>[@mantine/hooks]</code> use-scroll-spy: Allow usage with refs (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9025">#9025</a>)</li>
<li><code>[@mantine/core]</code> ColorInput: Add support for fullWidth prop (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9061">#9061</a>)</li>
<li><code>[@mantine/core]</code> Checkbox: Fix incottect indeterminate aria attributes handling in Checkbox.Card (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9095">#9095</a>)</li>
<li><code>[@mantine/core]</code> FloatingIndicator: Fix position and size calculation under scaled ancestors (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9071">#9071</a>)</li>
<li><code>[@mantine/core]</code> Tooltip: Add interactive prop support (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9072">#9072</a>)</li>
<li><code>[@mantine/core]</code> Cascader: Add safe area polygon support</li>
<li><code>[@mantine/core]</code> PasswordInput: Add option to change whether the visibility toggle is focusable (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9090">#9090</a>)</li>
<li><code>[@mantine/charts]</code> ScatterChart: Add option to add second y axis</li>
<li><code>[@mantine/schedule]</code> YearView: Add <code>renderDay</code> prop support</li>
<li><code>[@mantine/schedule]</code> YearView: Add option to hide weekend days</li>
<li><code>[@mantine/core]</code> InputWrapper: Fix <code>component: div</code> triggering typescript error if passed to <code>descriptionProps</code></li>
<li><code>[@mantine/schedule]</code> ResourcesMonthView: Add option to resize events</li>
<li><code>[@mantine/core]</code> FloatingWindow: Add support for  <code>onSizeChange</code> and <code>onResizeStart</code> props (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/colors-generator/issues/9085">#9085</a>)</li>
</ul>
<h2>9.5.0 🤖</h2>
<p><a href="https://mantine.dev/changelog/9-5-0">View changelog with demos on mantine.dev website</a></p>
<h2>Support Mantine development</h2>
<p>You can now sponsor Mantine development with <a href="https://opencollective.com/mantinedev">OpenCollective</a>.
All funds are used to improve Mantine and create new features and components.</p>
<h2>Migration to oxc</h2>
<p>Mantine has migrated its linting and formatting toolchain from ESLint and Prettier
to <a href="https://oxc.rs">oxc</a> – <a href="https://www.npmjs.com/package/oxlint">oxlint</a> is now used
as the linter and <a href="https://www.npmjs.com/package/oxfmt">oxfmt</a> as the formatter. Both
tools are written in Rust and are significantly faster than their predecessors, which
makes linting and formatting the entire codebase almost instant.</p>
<p>The shared configuration is available as a new
<a href="https://mantine.dev/oxc-config-mantine">oxc-config-mantine</a> package (a replacement for the previous</p>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/mantinedev/mantine/commit/8a284e2c2c53a9cb6f39f5dc389bf41b7a2073f8"><code>8a284e2</code></a> [release] Version: 9.5.2</li>
<li><a href="https://github.com/mantinedev/mantine/commit/0f57eaf5ae90c9e870fbb2a4cdd61a1d58c4c01d"><code>0f57eaf</code></a> [release] Version: 9.5.1</li>
<li><a href="https://github.com/mantinedev/mantine/commit/ca9bc6f156b63f1a10918d94ec31ec18e4e60546"><code>ca9bc6f</code></a> [release] Version: 9.5.1-alpha.1</li>
<li><a href="https://github.com/mantinedev/mantine/commit/8f1ad1bbe545c9cafafc5aef5b059d3d48e676a6"><code>8f1ad1b</code></a> [release] Version: 9.5.1-alpha.0</li>
<li><a href="https://github.com/mantinedev/mantine/commit/f1d330613f54dc9319d176e6d8ba5ebff233da18"><code>f1d3306</code></a> [release] Version: 9.5.0</li>
<li><a href="https://github.com/mantinedev/mantine/commit/732056219a0283f5822001981d7f652e632c4c87"><code>7320562</code></a> [release] Version: 9.4.3</li>
<li><a href="https://github.com/mantinedev/mantine/commit/de21a8203060ba29441ab7623244339748e4319d"><code>de21a82</code></a> [release] Version: 9.4.3-alpha.0</li>
<li><a href="https://github.com/mantinedev/mantine/commit/e5752de4067bd58f6cdd970660b3c8469a56d4e5"><code>e5752de</code></a> [release] Version: 9.4.2</li>
<li><a href="https://github.com/mantinedev/mantine/commit/d709e0bc277255c2a857f138cc694028273d8697"><code>d709e0b</code></a> [release] Version: 9.4.1</li>
<li><a href="https://github.com/mantinedev/mantine/commit/75d5ab5b419f3aa560bb56fc1d75d7815c5fb2f8"><code>75d5ab5</code></a> [release] Version: 9.4.0</li>
<li>Additional commits viewable in <a href="https://github.com/mantinedev/mantine/commits/9.5.2/packages/@mantine/colors-generator">compare view</a></li>
</ul>
</details>
<details>
<summary>Maintainer changes</summary>
<p>This version was pushed to npm by <a href="https://www.npmjs.com/~GitHub%20Actions">GitHub Actions</a>, a new releaser for <code>@​mantine/colors-generator</code> since your current version.</p>
</details>
<br />

Updates `@mantine/core` from 9.1.1 to 9.5.2
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/mantinedev/mantine/releases">@​mantine/core's releases</a>.</em></p>
<blockquote>
<h2>9.5.2</h2>
<ul>
<li><code>[@mantine/hooks]</code> use-debounced-value: Fix <code>leading: true</code> firing multiple times per burst and emiting a stale value (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/core/issues/9119">#9119</a>)</li>
<li><code>[@mantine/schedule]</code> Fix recurring events not working with timzones (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/core/issues/9112">#9112</a>)</li>
<li><code>[@mantine/dates]</code> Fix <code>minDate</code> used for default date in some cases (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/core/issues/9117">#9117</a>)</li>
<li><code>[@mantine/core]</code> Tooltip: Fix tooltip setting NaN in top/left position style when event position values cannot be read (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/core/issues/9131">#9131</a>)</li>
<li><code>[@mantine/dates]</code> TimePicker: Fix incorrect focus handling of partially filled hours field (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/core/issues/9128">#9128</a>)</li>
<li><code>[@mantine/core]</code> RollingNumber: Fix incorrect copy event handling (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/core/issues/9132">#9132</a>)</li>
<li><code>[@mantine/core]</code> Notification: Fix incorrect <code>closeButtonProps</code> type (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/core/issues/9134">#9134</a>)</li>
<li><code>[@mantine/code-highlight]</code> Add support for lazy languages loading (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/core/issues/9141">#9141</a>)</li>
<li><code>[@mantine/code-highlight]</code> CodeHighlight: Add prop to keep indentation of the first line of the code block (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/core/issues/9140">#9140</a>)</li>
<li><code>[@mantine/dates]</code> Add missing formatting functions to MiniCalendarm DateInput and YarsList components</li>
<li><code>[@mantine/schedule]</code> WeekView: Improve performance of events positioning algorithm (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/core/issues/9075">#9075</a>)</li>
<li><code>[@mantine/form]</code> Add new useWatchValue hook</li>
<li><code>[@mantine/core]</code> Fix Combobox-based components not working correctly with Chrome autocomplete</li>
</ul>
<h2>9.5.1</h2>
<ul>
<li><code>[@mantine/tiptap]</code> Fix controls being initially disabledbefore element is focused</li>
<li><code>[@mantine/tiptap]</code> Fix source code control wrapping content with extra p tag</li>
<li><code>[@mantine/hooks]</code> use-scroll-spy: Allow usage with refs (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/core/issues/9025">#9025</a>)</li>
<li><code>[@mantine/core]</code> ColorInput: Add support for fullWidth prop (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/core/issues/9061">#9061</a>)</li>
<li><code>[@mantine/core]</code> Checkbox: Fix incottect indeterminate aria attributes handling in Checkbox.Card (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/core/issues/9095">#9095</a>)</li>
<li><code>[@mantine/core]</code> FloatingIndicator: Fix position and size calculation under scaled ancestors (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/core/issues/9071">#9071</a>)</li>
<li><code>[@mantine/core]</code> Tooltip: Add interactive prop support (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/core/issues/9072">#9072</a>)</li>
<li><code>[@mantine/core]</code> Cascader: Add safe area polygon support</li>
<li><code>[@mantine/core]</code> PasswordInput: Add option to change whether the visibility toggle is focusable (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/core/issues/9090">#9090</a>)</li>
<li><code>[@mantine/charts]</code> ScatterChart: Add option to add second y axis</li>
<li><code>[@mantine/schedule]</code> YearView: Add <code>renderDay</code> prop support</li>
<li><code>[@mantine/schedule]</code> YearView: Add option to hide weekend days</li>
<li><code>[@mantine/core]</code> InputWrapper: Fix <code>component: div</code> triggering typescript error if passed to <code>descriptionProps</code></li>
<li><code>[@mantine/schedule]</code> ResourcesMonthView: Add option to resize events</li>
<li><code>[@mantine/core]</code> FloatingWindow: Add support for  <code>onSizeChange</code> and <code>onResizeStart</code> props (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/core/issues/9085">#9085</a>)</li>
</ul>
<h2>9.5.0 🤖</h2>
<p><a href="https://mantine.dev/changelog/9-5-0">View changelog with demos on mantine.dev website</a></p>
<h2>Support Mantine development</h2>
<p>You can now sponsor Mantine development with <a href="https://opencollective.com/mantinedev">OpenCollective</a>.
All funds are used to improve Mantine and create new features and components.</p>
<h2>Migration to oxc</h2>
<p>Mantine has migrated its linting and formatting toolchain from ESLint and Prettier
to <a href="https://oxc.rs">oxc</a> – <a href="https://www.npmjs.com/package/oxlint">oxlint</a> is now used
as the linter and <a href="https://www.npmjs.com/package/oxfmt">oxfmt</a> as the formatter. Both
tools are written in Rust and are significantly faster than their predecessors, which
makes linting and formatting the entire codebase almost instant.</p>
<p>The shared configuration is available as a new
<a href="https://mantine.dev/oxc-config-mantine">oxc-config-mantine</a> package (a replacement for the previous</p>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/mantinedev/mantine/commit/8a284e2c2c53a9cb6f39f5dc389bf41b7a2073f8"><code>8a284e2</code></a> [release] Version: 9.5.2</li>
<li><a href="https://github.com/mantinedev/mantine/commit/a2e25fe891429f5ae4aa4c1a4593610a91ae743d"><code>a2e25fe</code></a> [<code>@​mantine/core</code>] Tooltip: Fix tooltip setting NaN in top/left position style w...</li>
<li><a href="https://github.com/mantinedev/mantine/commit/a88b24cf9d379fa3d6cbb31b449e6a5dcc2a789d"><code>a88b24c</code></a> Merge branch 'master' of github.com:mantinedev/mantine</li>
<li><a href="https://github.com/mantinedev/mantine/commit/4509931406a34ca8695a4bb7ff26bd686ffe659d"><code>4509931</code></a> [<code>@​mantine/core</code>] RollingNumber: Fix incorrect copy event handling (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/core/issues/9132">#9132</a>)</li>
<li><a href="https://github.com/mantinedev/mantine/commit/20b9a5e6d1de35f44cf561fd22ff005d07cf656a"><code>20b9a5e</code></a> [<code>@​mantine/core</code>] Notification: Fix incorrect <code>closeButtonProps</code> type (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/core/issues/9134">#9134</a>)</li>
<li><a href="https://github.com/mantinedev/mantine/commit/681c9fee1f17ce77e9c6983d960eeec4c72f4fc2"><code>681c9fe</code></a> [mantine.dev] Fix missing ComboboxPopover styles API documentation (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/core/issues/9135">#9135</a>)</li>
<li><a href="https://github.com/mantinedev/mantine/commit/b30ae5b1aef44945395cbeb40a8f987897e03740"><code>b30ae5b</code></a> [<code>@​mantine/core</code>] Fix Combobox-based components not working correctly with Chro...</li>
<li><a href="https://github.com/mantinedev/mantine/commit/0f57eaf5ae90c9e870fbb2a4cdd61a1d58c4c01d"><code>0f57eaf</code></a> [release] Version: 9.5.1</li>
<li><a href="https://github.com/mantinedev/mantine/commit/58abe86af4153db7639966aba6ad5521b02b1c96"><code>58abe86</code></a> [<code>@​mantine/core</code>] ColorInput: Add support for fullWidth prop (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/core/issues/9061">#9061</a>)</li>
<li><a href="https://github.com/mantinedev/mantine/commit/34db05f19c3115e6b749546e7ae9d07fdf9c4ffe"><code>34db05f</code></a> Merge branch 'master' of github.com:mantinedev/mantine</li>
<li>Additional commits viewable in <a href="https://github.com/mantinedev/mantine/commits/9.5.2/packages/@mantine/core">compare view</a></li>
</ul>
</details>
<details>
<summary>Maintainer changes</summary>
<p>This version was pushed to npm by <a href="https://www.npmjs.com/~GitHub%20Actions">GitHub Actions</a>, a new releaser for <code>@​mantine/core</code> since your current version.</p>
</details>
<br />

Updates `@mantine/dates` from 9.1.1 to 9.5.2
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/mantinedev/mantine/releases">@​mantine/dates's releases</a>.</em></p>
<blockquote>
<h2>9.5.2</h2>
<ul>
<li><code>[@mantine/hooks]</code> use-debounced-value: Fix <code>leading: true</code> firing multiple times per burst and emiting a stale value (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dates/issues/9119">#9119</a>)</li>
<li><code>[@mantine/schedule]</code> Fix recurring events not working with timzones (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dates/issues/9112">#9112</a>)</li>
<li><code>[@mantine/dates]</code> Fix <code>minDate</code> used for default date in some cases (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dates/issues/9117">#9117</a>)</li>
<li><code>[@mantine/core]</code> Tooltip: Fix tooltip setting NaN in top/left position style when event position values cannot be read (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dates/issues/9131">#9131</a>)</li>
<li><code>[@mantine/dates]</code> TimePicker: Fix incorrect focus handling of partially filled hours field (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dates/issues/9128">#9128</a>)</li>
<li><code>[@mantine/core]</code> RollingNumber: Fix incorrect copy event handling (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dates/issues/9132">#9132</a>)</li>
<li><code>[@mantine/core]</code> Notification: Fix incorrect <code>closeButtonProps</code> type (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dates/issues/9134">#9134</a>)</li>
<li><code>[@mantine/code-highlight]</code> Add support for lazy languages loading (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dates/issues/9141">#9141</a>)</li>
<li><code>[@mantine/code-highlight]</code> CodeHighlight: Add prop to keep indentation of the first line of the code block (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dates/issues/9140">#9140</a>)</li>
<li><code>[@mantine/dates]</code> Add missing formatting functions to MiniCalendarm DateInput and YarsList components</li>
<li><code>[@mantine/schedule]</code> WeekView: Improve performance of events positioning algorithm (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dates/issues/9075">#9075</a>)</li>
<li><code>[@mantine/form]</code> Add new useWatchValue hook</li>
<li><code>[@mantine/core]</code> Fix Combobox-based components not working correctly with Chrome autocomplete</li>
</ul>
<h2>9.5.1</h2>
<ul>
<li><code>[@mantine/tiptap]</code> Fix controls being initially disabledbefore element is focused</li>
<li><code>[@mantine/tiptap]</code> Fix source code control wrapping content with extra p tag</li>
<li><code>[@mantine/hooks]</code> use-scroll-spy: Allow usage with refs (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dates/issues/9025">#9025</a>)</li>
<li><code>[@mantine/core]</code> ColorInput: Add support for fullWidth prop (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dates/issues/9061">#9061</a>)</li>
<li><code>[@mantine/core]</code> Checkbox: Fix incottect indeterminate aria attributes handling in Checkbox.Card (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dates/issues/9095">#9095</a>)</li>
<li><code>[@mantine/core]</code> FloatingIndicator: Fix position and size calculation under scaled ancestors (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dates/issues/9071">#9071</a>)</li>
<li><code>[@mantine/core]</code> Tooltip: Add interactive prop support (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dates/issues/9072">#9072</a>)</li>
<li><code>[@mantine/core]</code> Cascader: Add safe area polygon support</li>
<li><code>[@mantine/core]</code> PasswordInput: Add option to change whether the visibility toggle is focusable (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dates/issues/9090">#9090</a>)</li>
<li><code>[@mantine/charts]</code> ScatterChart: Add option to add second y axis</li>
<li><code>[@mantine/schedule]</code> YearView: Add <code>renderDay</code> prop support</li>
<li><code>[@mantine/schedule]</code> YearView: Add option to hide weekend days</li>
<li><code>[@mantine/core]</code> InputWrapper: Fix <code>component: div</code> triggering typescript error if passed to <code>descriptionProps</code></li>
<li><code>[@mantine/schedule]</code> ResourcesMonthView: Add option to resize events</li>
<li><code>[@mantine/core]</code> FloatingWindow: Add support for  <code>onSizeChange</code> and <code>onResizeStart</code> props (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dates/issues/9085">#9085</a>)</li>
</ul>
<h2>9.5.0 🤖</h2>
<p><a href="https://mantine.dev/changelog/9-5-0">View changelog with demos on mantine.dev website</a></p>
<h2>Support Mantine development</h2>
<p>You can now sponsor Mantine development with <a href="https://opencollective.com/mantinedev">OpenCollective</a>.
All funds are used to improve Mantine and create new features and components.</p>
<h2>Migration to oxc</h2>
<p>Mantine has migrated its linting and formatting toolchain from ESLint and Prettier
to <a href="https://oxc.rs">oxc</a> – <a href="https://www.npmjs.com/package/oxlint">oxlint</a> is now used
as the linter and <a href="https://www.npmjs.com/package/oxfmt">oxfmt</a> as the formatter. Both
tools are written in Rust and are significantly faster than their predecessors, which
makes linting and formatting the entire codebase almost instant.</p>
<p>The shared configuration is available as a new
<a href="https://mantine.dev/oxc-config-mantine">oxc-config-mantine</a> package (a replacement for the previous</p>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/mantinedev/mantine/commit/8a284e2c2c53a9cb6f39f5dc389bf41b7a2073f8"><code>8a284e2</code></a> [release] Version: 9.5.2</li>
<li><a href="https://github.com/mantinedev/mantine/commit/38a41b273813f785b08983422283ca1f6d201c97"><code>38a41b2</code></a> [<code>@​mantine/dates</code>] Fix <code>minDate</code> used for default date in some cases (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dates/issues/9117">#9117</a>)</li>
<li><a href="https://github.com/mantinedev/mantine/commit/7f499c0c42e0a1533484644328eaf412b46a9a21"><code>7f499c0</code></a> [<code>@​mantine/dates</code>] TimePicker: Fix incorrect focus handling of partially filled...</li>
<li><a href="https://github.com/mantinedev/mantine/commit/6b82799329d41a10237c13e484369a035df334fd"><code>6b82799</code></a> [<code>@​mantine/dates</code>] Add missing formatting functions to MiniCalendarm DateInput ...</li>
<li><a href="https://github.com/mantinedev/mantine/commit/0f57eaf5ae90c9e870fbb2a4cdd61a1d58c4c01d"><code>0f57eaf</code></a> [release] Version: 9.5.1</li>
<li><a href="https://github.com/mantinedev/mantine/commit/ca9bc6f156b63f1a10918d94ec31ec18e4e60546"><code>ca9bc6f</code></a> [release] Version: 9.5.1-alpha.1</li>
<li><a href="https://github.com/mantinedev/mantine/commit/8f1ad1bbe545c9cafafc5aef5b059d3d48e676a6"><code>8f1ad1b</code></a> [release] Version: 9.5.1-alpha.0</li>
<li><a href="https://github.com/mantinedev/mantine/commit/f1d330613f54dc9319d176e6d8ba5ebff233da18"><code>f1d3306</code></a> [release] Version: 9.5.0</li>
<li><a href="https://github.com/mantinedev/mantine/commit/732056219a0283f5822001981d7f652e632c4c87"><code>7320562</code></a> [release] Version: 9.4.3</li>
<li><a href="https://github.com/mantinedev/mantine/commit/170c45a5feed2386a464a7f05ae3daf6379cea04"><code>170c45a</code></a> Merge branch '9.5'</li>
<li>Additional commits viewable in <a href="https://github.com/mantinedev/mantine/commits/9.5.2/packages/@mantine/dates">compare view</a></li>
</ul>
</details>
<details>
<summary>Maintainer changes</summary>
<p>This version was pushed to npm by <a href="https://www.npmjs.com/~GitHub%20Actions">GitHub Actions</a>, a new releaser for <code>@​mantine/dates</code> since your current version.</p>
</details>
<br />

Updates `@mantine/dropzone` from 9.1.1 to 9.5.2
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/mantinedev/mantine/releases">@​mantine/dropzone's releases</a>.</em></p>
<blockquote>
<h2>9.5.2</h2>
<ul>
<li><code>[@mantine/hooks]</code> use-debounced-value: Fix <code>leading: true</code> firing multiple times per burst and emiting a stale value (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dropzone/issues/9119">#9119</a>)</li>
<li><code>[@mantine/schedule]</code> Fix recurring events not working with timzones (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dropzone/issues/9112">#9112</a>)</li>
<li><code>[@mantine/dates]</code> Fix <code>minDate</code> used for default date in some cases (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dropzone/issues/9117">#9117</a>)</li>
<li><code>[@mantine/core]</code> Tooltip: Fix tooltip setting NaN in top/left position style when event position values cannot be read (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dropzone/issues/9131">#9131</a>)</li>
<li><code>[@mantine/dates]</code> TimePicker: Fix incorrect focus handling of partially filled hours field (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dropzone/issues/9128">#9128</a>)</li>
<li><code>[@mantine/core]</code> RollingNumber: Fix incorrect copy event handling (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dropzone/issues/9132">#9132</a>)</li>
<li><code>[@mantine/core]</code> Notification: Fix incorrect <code>closeButtonProps</code> type (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dropzone/issues/9134">#9134</a>)</li>
<li><code>[@mantine/code-highlight]</code> Add support for lazy languages loading (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dropzone/issues/9141">#9141</a>)</li>
<li><code>[@mantine/code-highlight]</code> CodeHighlight: Add prop to keep indentation of the first line of the code block (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dropzone/issues/9140">#9140</a>)</li>
<li><code>[@mantine/dates]</code> Add missing formatting functions to MiniCalendarm DateInput and YarsList components</li>
<li><code>[@mantine/schedule]</code> WeekView: Improve performance of events positioning algorithm (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dropzone/issues/9075">#9075</a>)</li>
<li><code>[@mantine/form]</code> Add new useWatchValue hook</li>
<li><code>[@mantine/core]</code> Fix Combobox-based components not working correctly with Chrome autocomplete</li>
</ul>
<h2>9.5.1</h2>
<ul>
<li><code>[@mantine/tiptap]</code> Fix controls being initially disabledbefore element is focused</li>
<li><code>[@mantine/tiptap]</code> Fix source code control wrapping content with extra p tag</li>
<li><code>[@mantine/hooks]</code> use-scroll-spy: Allow usage with refs (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dropzone/issues/9025">#9025</a>)</li>
<li><code>[@mantine/core]</code> ColorInput: Add support for fullWidth prop (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dropzone/issues/9061">#9061</a>)</li>
<li><code>[@mantine/core]</code> Checkbox: Fix incottect indeterminate aria attributes handling in Checkbox.Card (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dropzone/issues/9095">#9095</a>)</li>
<li><code>[@mantine/core]</code> FloatingIndicator: Fix position and size calculation under scaled ancestors (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dropzone/issues/9071">#9071</a>)</li>
<li><code>[@mantine/core]</code> Tooltip: Add interactive prop support (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dropzone/issues/9072">#9072</a>)</li>
<li><code>[@mantine/core]</code> Cascader: Add safe area polygon support</li>
<li><code>[@mantine/core]</code> PasswordInput: Add option to change whether the visibility toggle is focusable (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dropzone/issues/9090">#9090</a>)</li>
<li><code>[@mantine/charts]</code> ScatterChart: Add option to add second y axis</li>
<li><code>[@mantine/schedule]</code> YearView: Add <code>renderDay</code> prop support</li>
<li><code>[@mantine/schedule]</code> YearView: Add option to hide weekend days</li>
<li><code>[@mantine/core]</code> InputWrapper: Fix <code>component: div</code> triggering typescript error if passed to <code>descriptionProps</code></li>
<li><code>[@mantine/schedule]</code> ResourcesMonthView: Add option to resize events</li>
<li><code>[@mantine/core]</code> FloatingWindow: Add support for  <code>onSizeChange</code> and <code>onResizeStart</code> props (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/dropzone/issues/9085">#9085</a>)</li>
</ul>
<h2>9.5.0 🤖</h2>
<p><a href="https://mantine.dev/changelog/9-5-0">View changelog with demos on mantine.dev website</a></p>
<h2>Support Mantine development</h2>
<p>You can now sponsor Mantine development with <a href="https://opencollective.com/mantinedev">OpenCollective</a>.
All funds are used to improve Mantine and create new features and components.</p>
<h2>Migration to oxc</h2>
<p>Mantine has migrated its linting and formatting toolchain from ESLint and Prettier
to <a href="https://oxc.rs">oxc</a> – <a href="https://www.npmjs.com/package/oxlint">oxlint</a> is now used
as the linter and <a href="https://www.npmjs.com/package/oxfmt">oxfmt</a> as the formatter. Both
tools are written in Rust and are significantly faster than their predecessors, which
makes linting and formatting the entire codebase almost instant.</p>
<p>The shared configuration is available as a new
<a href="https://mantine.dev/oxc-config-mantine">oxc-config-mantine</a> package (a replacement for the previous</p>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/mantinedev/mantine/commit/8a284e2c2c53a9cb6f39f5dc389bf41b7a2073f8"><code>8a284e2</code></a> [release] Version: 9.5.2</li>
<li><a href="https://github.com/mantinedev/mantine/commit/0f57eaf5ae90c9e870fbb2a4cdd61a1d58c4c01d"><code>0f57eaf</code></a> [release] Version: 9.5.1</li>
<li><a href="https://github.com/mantinedev/mantine/commit/ca9bc6f156b63f1a10918d94ec31ec18e4e60546"><code>ca9bc6f</code></a> [release] Version: 9.5.1-alpha.1</li>
<li><a href="https://github.com/mantinedev/mantine/commit/8f1ad1bbe545c9cafafc5aef5b059d3d48e676a6"><code>8f1ad1b</code></a> [release] Version: 9.5.1-alpha.0</li>
<li><a href="https://github.com/mantinedev/mantine/commit/f1d330613f54dc9319d176e6d8ba5ebff233da18"><code>f1d3306</code></a> [release] Version: 9.5.0</li>
<li><a href="https://github.com/mantinedev/mantine/commit/732056219a0283f5822001981d7f652e632c4c87"><code>7320562</code></a> [release] Version: 9.4.3</li>
<li><a href="https://github.com/mantinedev/mantine/commit/de21a8203060ba29441ab7623244339748e4319d"><code>de21a82</code></a> [release] Version: 9.4.3-alpha.0</li>
<li><a href="https://github.com/mantinedev/mantine/commit/e5752de4067bd58f6cdd970660b3c8469a56d4e5"><code>e5752de</code></a> [release] Version: 9.4.2</li>
<li><a href="https://github.com/mantinedev/mantine/commit/d709e0bc277255c2a857f138cc694028273d8697"><code>d709e0b</code></a> [release] Version: 9.4.1</li>
<li><a href="https://github.com/mantinedev/mantine/commit/75d5ab5b419f3aa560bb56fc1d75d7815c5fb2f8"><code>75d5ab5</code></a> [release] Version: 9.4.0</li>
<li>Additional commits viewable in <a href="https://github.com/mantinedev/mantine/commits/9.5.2/packages/@mantine/dropzone">compare view</a></li>
</ul>
</details>
<details>
<summary>Maintainer changes</summary>
<p>This version was pushed to npm by <a href="https://www.npmjs.com/~GitHub%20Actions">GitHub Actions</a>, a new releaser for <code>@​mantine/dropzone</code> since your current version.</p>
</details>
<br />

Updates `@mantine/form` from 9.1.1 to 9.5.2
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/mantinedev/mantine/releases">@​mantine/form's releases</a>.</em></p>
<blockquote>
<h2>9.5.2</h2>
<ul>
<li><code>[@mantine/hooks]</code> use-debounced-value: Fix <code>leading: true</code> firing multiple times per burst and emiting a stale value (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/form/issues/9119">#9119</a>)</li>
<li><code>[@mantine/schedule]</code> Fix recurring events not working with timzones (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/form/issues/9112">#9112</a>)</li>
<li><code>[@mantine/dates]</code> Fix <code>minDate</code> used for default date in some cases (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/form/issues/9117">#9117</a>)</li>
<li><code>[@mantine/core]</code> Tooltip: Fix tooltip setting NaN in top/left position style when event position values cannot be read (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/form/issues/9131">#9131</a>)</li>
<li><code>[@mantine/dates]</code> TimePicker: Fix incorrect focus handling of partially filled hours field (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/form/issues/9128">#9128</a>)</li>
<li><code>[@mantine/core]</code> RollingNumber: Fix incorrect copy event handling (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/form/issues/9132">#9132</a>)</li>
<li><code>[@mantine/core]</code> Notification: Fix incorrect <code>closeButtonProps</code> type (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/form/issues/9134">#9134</a>)</li>
<li><code>[@mantine/code-highlight]</code> Add support for lazy languages loading (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/form/issues/9141">#9141</a>)</li>
<li><code>[@mantine/code-highlight]</code> CodeHighlight: Add prop to keep indentation of the first line of the code block (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/form/issues/9140">#9140</a>)</li>
<li><code>[@mantine/dates]</code> Add missing formatting functions to MiniCalendarm DateInput and YarsList components</li>
<li><code>[@mantine/schedule]</code> WeekView: Improve performance of events positioning algorithm (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/form/issues/9075">#9075</a>)</li>
<li><code>[@mantine/form]</code> Add new useWatchValue hook</li>
<li><code>[@mantine/core]</code> Fix Combobox-based components not working correctly with Chrome autocomplete</li>
</ul>
<h2>9.5.1</h2>
<ul>
<li><code>[@mantine/tiptap]</code> Fix controls being initially disabledbefore element is focused</li>
<li><code>[@mantine/tiptap]</code> Fix source code control wrapping content with extra p tag</li>
<li><code>[@mantine/hooks]</code> use-scroll-spy: Allow usage with refs (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/form/issues/9025">#9025</a>)</li>
<li><code>[@mantine/core]</code> ColorInput: Add support for fullWidth prop (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/form/issues/9061">#9061</a>)</li>
<li><code>[@mantine/core]</code> Checkbox: Fix incottect indeterminate aria attributes handling in Checkbox.Card (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/form/issues/9095">#9095</a>)</li>
<li><code>[@mantine/core]</code> FloatingIndicator: Fix position and size calculation under scaled ancestors (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/form/issues/9071">#9071</a>)</li>
<li><code>[@mantine/core]</code> Tooltip: Add interactive prop support (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/form/issues/9072">#9072</a>)</li>
<li><code>[@mantine/core]</code> Cascader: Add safe area polygon support</li>
<li><code>[@mantine/core]</code> PasswordInput: Add option to change whether the visibility toggle is focusable (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/form/issues/9090">#9090</a>)</li>
<li><code>[@mantine/charts]</code> ScatterChart: Add option to add second y axis</li>
<li><code>[@mantine/schedule]</code> YearView: Add <code>renderDay</code> prop support</li>
<li><code>[@mantine/schedule]</code> YearView: Add option to hide weekend days</li>
<li><code>[@mantine/core]</code> InputWrapper: Fix <code>component: div</code> triggering typescript error if passed to <code>descriptionProps</code></li>
<li><code>[@mantine/schedule]</code> ResourcesMonthView: Add option to resize events</li>
<li><code>[@mantine/core]</code> FloatingWindow: Add support for  <code>onSizeChange</code> and <code>onResizeStart</code> props (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/form/issues/9085">#9085</a>)</li>
</ul>
<h2>9.5.0 🤖</h2>
<p><a href="https://mantine.dev/changelog/9-5-0">View changelog with demos on mantine.dev website</a></p>
<h2>Support Mantine development</h2>
<p>You can now sponsor Mantine development with <a href="https://opencollective.com/mantinedev">OpenCollective</a>.
All funds are used to improve Mantine and create new features and components.</p>
<h2>Migration to oxc</h2>
<p>Mantine has migrated its linting and formatting toolchain from ESLint and Prettier
to <a href="https://oxc.rs">oxc</a> – <a href="https://www.npmjs.com/package/oxlint">oxlint</a> is now used
as the linter and <a href="https://www.npmjs.com/package/oxfmt">oxfmt</a> as the formatter. Both
tools are written in Rust and are significantly faster than their predecessors, which
makes linting and formatting the entire codebase almost instant.</p>
<p>The shared configuration is available as a new
<a href="https://mantine.dev/oxc-config-mantine">oxc-config-mantine</a> package (a replacement for the previous</p>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/mantinedev/mantine/commit/8a284e2c2c53a9cb6f39f5dc389bf41b7a2073f8"><code>8a284e2</code></a> [release] Version: 9.5.2</li>
<li><a href="https://github.com/mantinedev/mantine/commit/698381d31dee39f3d6f5ac58df7d6968eee01cb8"><code>698381d</code></a> [<code>@​mantine/form</code>] Add new useWatchValue hook</li>
<li><a href="https://github.com/mantinedev/mantine/commit/0f57eaf5ae90c9e870fbb2a4cdd61a1d58c4c01d"><code>0f57eaf</code></a> [release] Version: 9.5.1</li>
<li><a href="https://github.com/mantinedev/mantine/commit/ca9bc6f156b63f1a10918d94ec31ec18e4e60546"><code>ca9bc6f</code></a> [release] Version: 9.5.1-alpha.1</li>
<li><a href="https://github.com/mantinedev/mantine/commit/8f1ad1bbe545c9cafafc5aef5b059d3d48e676a6"><code>8f1ad1b</code></a> [release] Version: 9.5.1-alpha.0</li>
<li><a href="https://github.com/mantinedev/mantine/commit/f1d330613f54dc9319d176e6d8ba5ebff233da18"><code>f1d3306</code></a> [release] Version: 9.5.0</li>
<li><a href="https://github.com/mantinedev/mantine/commit/732056219a0283f5822001981d7f652e632c4c87"><code>7320562</code></a> [release] Version: 9.4.3</li>
<li><a href="https://github.com/mantinedev/mantine/commit/de21a8203060ba29441ab7623244339748e4319d"><code>de21a82</code></a> [release] Version: 9.4.3-alpha.0</li>
<li><a href="https://github.com/mantinedev/mantine/commit/e5752de4067bd58f6cdd970660b3c8469a56d4e5"><code>e5752de</code></a> [release] Version: 9.4.2</li>
<li><a href="https://github.com/mantinedev/mantine/commit/1d68be7025ceca3619eda9db00b9395c53b75c0a"><code>1d68be7</code></a> [<code>@​mantine/form</code>] Fix async validation with debounce on initial keystroke of em...</li>
<li>Additional commits viewable in <a href="https://github.com/mantinedev/mantine/commits/9.5.2/packages/@mantine/form">compare view</a></li>
</ul>
</details>
<details>
<summary>Maintainer changes</summary>
<p>This version was pushed to npm by <a href="https://www.npmjs.com/~GitHub%20Actions">GitHub Actions</a>, a new releaser for <code>@​mantine/form</code> since your current version.</p>
</details>
<br />

Updates `@mantine/hooks` from 9.1.1 to 9.5.2
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/mantinedev/mantine/releases">@​mantine/hooks's releases</a>.</em></p>
<blockquote>
<h2>9.5.2</h2>
<ul>
<li><code>[@mantine/hooks]</code> use-debounced-value: Fix <code>leading: true</code> firing multiple times per burst and emiting a stale value (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/hooks/issues/9119">#9119</a>)</li>
<li><code>[@mantine/schedule]</code> Fix recurring events not working with timzones (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/hooks/issues/9112">#9112</a>)</li>
<li><code>[@mantine/dates]</code> Fix <code>minDate</code> used for default date in some cases (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/hooks/issues/9117">#9117</a>)</li>
<li><code>[@mantine/core]</code> Tooltip: Fix tooltip setting NaN in top/left position style when event position values cannot be read (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/hooks/issues/9131">#9131</a>)</li>
<li><code>[@mantine/dates]</code> TimePicker: Fix incorrect focus handling of partially filled hours field (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/hooks/issues/9128">#9128</a>)</li>
<li><code>[@mantine/core]</code> RollingNumber: Fix incorrect copy event handling (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/hooks/issues/9132">#9132</a>)</li>
<li><code>[@mantine/core]</code> Notification: Fix incorrect <code>closeButtonProps</code> type (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/hooks/issues/9134">#9134</a>)</li>
<li><code>[@mantine/code-highlight]</code> Add support for lazy languages loading (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/hooks/issues/9141">#9141</a>)</li>
<li><code>[@mantine/code-highlight]</code> CodeHighlight: Add prop to keep indentation of the first line of the code block (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/hooks/issues/9140">#9140</a>)</li>
<li><code>[@mantine/dates]</code> Add missing formatting functions to MiniCalendarm DateInput and YarsList components</li>
<li><code>[@mantine/schedule]</code> WeekView: Improve performance of events positioning algorithm (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/hooks/issues/9075">#9075</a>)</li>
<li><code>[@mantine/form]</code> Add new useWatchValue hook</li>
<li><code>[@mantine/core]</code> Fix Combobox-based components not working correctly with Chrome autocomplete</li>
</ul>
<h2>9.5.1</h2>
<ul>
<li><code>[@mantine/tiptap]</code> Fix controls being initially disabledbefore element is focused</li>
<li><code>[@mantine/tiptap]</code> Fix source code control wrapping content with extra p tag</li>
<li><code>[@mantine/hooks]</code> use-scroll-spy: Allow usage with refs (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/hooks/issues/9025">#9025</a>)</li>
<li><code>[@mantine/core]</code> ColorInput: Add support for fullWidth prop (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/hooks/issues/9061">#9061</a>)</li>
<li><code>[@mantine/core]</code> Checkbox: Fix incottect indeterminate aria attributes handling in Checkbox.Card (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/hooks/issues/9095">#9095</a>)</li>
<li><code>[@mantine/core]</code> FloatingIndicator: Fix position and size calculation under scaled ancestors (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/hooks/issues/9071">#9071</a>)</li>
<li><code>[@mantine/core]</code> Tooltip: Add interactive prop support (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/hooks/issues/9072">#9072</a>)</li>
<li><code>[@mantine/core]</code> Cascader: Add safe area polygon support</li>
<li><code>[@mantine/core]</code> PasswordInput: Add option to change whether the visibility toggle is focusable (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/hooks/issues/9090">#9090</a>)</li>
<li><code>[@mantine/charts]</code> ScatterChart: Add option to add second y axis</li>
<li><code>[@mantine/schedule]</code> YearView: Add <code>renderDay</code> prop support</li>
<li><code>[@mantine/schedule]</code> YearView: Add option to hide weekend days</li>
<li><code>[@mantine/core]</code> InputWrapper: Fix <code>component: div</code> triggering typescript error if passed to <code>descriptionProps</code></li>
<li><code>[@mantine/schedule]</code> ResourcesMonthView: Add option to resize events</li>
<li><code>[@mantine/core]</code> FloatingWindow: Add support for  <code>onSizeChange</code> and <code>onResizeStart</code> props (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/hooks/issues/9085">#9085</a>)</li>
</ul>
<h2>9.5.0 🤖</h2>
<p><a href="https://mantine.dev/changelog/9-5-0">View changelog with demos on mantine.dev website</a></p>
<h2>Support Mantine development</h2>
<p>You can now sponsor Mantine development with <a href="https://opencollective.com/mantinedev">OpenCollective</a>.
All funds are used to improve Mantine and create new features and components.</p>
<h2>Migration to oxc</h2>
<p>Mantine has migrated its linting and formatting toolchain from ESLint and Prettier
to <a href="https://oxc.rs">oxc</a> – <a href="https://www.npmjs.com/package/oxlint">oxlint</a> is now used
as the linter and <a href="https://www.npmjs.com/package/oxfmt">oxfmt</a> as the formatter. Both
tools are written in Rust and are significantly faster than their predecessors, which
makes linting and formatting the entire codebase almost instant.</p>
<p>The shared configuration is available as a new
<a href="https://mantine.dev/oxc-config-mantine">oxc-config-mantine</a> package (a replacement for the previous</p>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/mantinedev/mantine/commit/8a284e2c2c53a9cb6f39f5dc389bf41b7a2073f8"><code>8a284e2</code></a> [release] Version: 9.5.2</li>
<li><a href="https://github.com/mantinedev/mantine/commit/1f93ee57929e7f100bad1d3308c6d0f4f8a6d1ed"><code>1f93ee5</code></a> [<code>@​mantine/hooks</code>] use-debounced-value: Fix <code>leading: true</code> firing multiple tim...</li>
<li><a href="https://github.com/mantinedev/mantine/commit/0f57eaf5ae90c9e870fbb2a4cdd61a1d58c4c01d"><code>0f57eaf</code></a> [release] Version: 9.5.1</li>
<li><a href="https://github.com/mantinedev/mantine/commit/ce00cdde77a5ff69d20ad8f29956cd898c50d619"><code>ce00cdd</code></a> [<code>@​mantine/hooks</code>] use-scroll-spy: Allow usage with refs (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/hooks/issues/9025">#9025</a>)</li>
<li><a href="https://github.com/mantinedev/mantine/commit/ca9bc6f156b63f1a10918d94ec31ec18e4e60546"><code>ca9bc6f</code></a> [release] Version: 9.5.1-alpha.1</li>
<li><a href="https://github.com/mantinedev/mantine/commit/8f1ad1bbe545c9cafafc5aef5b059d3d48e676a6"><code>8f1ad1b</code></a> [release] Version: 9.5.1-alpha.0</li>
<li><a href="https://github.com/mantinedev/mantine/commit/953192e2207722a43568468d0db8d20eceb21307"><code>953192e</code></a> [<code>@​mantine/core</code>] FloatingWindow: Add support for  <code>onSizeChange</code> and `onResize...</li>
<li><a href="https://github.com/mantinedev/mantine/commit/f1d330613f54dc9319d176e6d8ba5ebff233da18"><code>f1d3306</code></a> [release] Version: 9.5.0</li>
<li><a href="https://github.com/mantinedev/mantine/commit/732056219a0283f5822001981d7f652e632c4c87"><code>7320562</code></a> [release] Version: 9.4.3</li>
<li><a href="https://github.com/mantinedev/mantine/commit/de21a8203060ba29441ab7623244339748e4319d"><code>de21a82</code></a> [release] Version: 9.4.3-alpha.0</li>
<li>Additional commits viewable in <a href="https://github.com/mantinedev/mantine/commits/9.5.2/packages/@mantine/hooks">compare view</a></li>
</ul>
</details>
<details>
<summary>Maintainer changes</summary>
<p>This version was pushed to npm by <a href="https://www.npmjs.com/~GitHub%20Actions">GitHub Actions</a>, a new releaser for <code>@​mantine/hooks</code> since your current version.</p>
</details>
<br />

Updates `@mantine/modals` from 9.1.1 to 9.5.2
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/mantinedev/mantine/releases">@​mantine/modals's releases</a>.</em></p>
<blockquote>
<h2>9.5.2</h2>
<ul>
<li><code>[@mantine/hooks]</code> use-debounced-value: Fix <code>leading: true</code> firing multiple times per burst and emiting a stale value (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/modals/issues/9119">#9119</a>)</li>
<li><code>[@mantine/schedule]</code> Fix recurring events not working with timzones (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/modals/issues/9112">#9112</a>)</li>
<li><code>[@mantine/dates]</code> Fix <code>minDate</code> used for default date in some cases (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/modals/issues/9117">#9117</a>)</li>
<li><code>[@mantine/core]</code> Tooltip: Fix tooltip setting NaN in top/left position style when event position values cannot be read (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/modals/issues/9131">#9131</a>)</li>
<li><code>[@mantine/dates]</code> TimePicker: Fix incorrect focus handling of partially filled hours field (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/modals/issues/9128">#9128</a>)</li>
<li><code>[@mantine/core]</code> RollingNumber: Fix incorrect copy event handling (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/modals/issues/9132">#9132</a>)</li>
<li><code>[@mantine/core]</code> Notification: Fix incorrect <code>closeButtonProps</code> type (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/modals/issues/9134">#9134</a>)</li>
<li><code>[@mantine/code-highlight]</code> Add support for lazy languages loading (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/modals/issues/9141">#9141</a>)</li>
<li><code>[@mantine/code-highlight]</code> CodeHighlight: Add prop to keep indentation of the first line of the code block (<a href="https://github.com/mantinedev/mantine/tree/HEAD/packages/@mantine/modals/issues/9140">#9140</a>)</li>
<li><code>[@mantine/dates]</code> Add missing form...

_Description has been truncated_

<details><summary>Comment — nathanpond, 2026-08-31</summary>

Validated in an isolated worktree on top of current `master` (supersedes archived-155, which conflicted after archived-152/archived-156 landed):

- `npm ci` → ESLint 0 errors / 411 warnings (at cap), `tsc -b` clean
- Full E2E: **140 passed / 0 failed / 2 skipped** (both skips pre-existing)
- One real finding: `@mantine/*` ≥ 9.4 `Textarea autosize` sets its own height inside the `ResizeObserver` callback observing the textarea, so a width change (chatbot sidebar resize) makes Chromium raise `ResizeObserver loop completed with undelivered notifications` as a page error. Bisected: 9.1.1/9.2.0/9.3.0 pass, 9.4.0/9.5.2 fail. It is benign browser notice text, not a JS exception — allowlisted in `ConsoleErrorGuard` with a rationale; tracked in archived-158 for removal once upstream defers the write. That is the one extra commit pushed onto this branch.
- No `bpmn-js` bump in this group, so the vendored bundle is unchanged.

</details>

---

## archived-159 — fix(yjs): accept documentName on POST /api/yjs/comment-event

`MERGED (merged 2026-08-31)` · nathanpond · opened 2026-08-31 · `fix/151-comment-event-contract` → `master`

Closes archived-151

## What

`POST /api/yjs/comment-event` now accepts the body the SPA has always sent — `{ documentName, threadId, commentId?, eventType }` — instead of requiring a bare `pageId`. Until now every comment create / reply / resolve / reopen / delete was a 400 (client only `console.warn`s), so the `content.comment.*` catalog entries were dead.

## How

`src/AutoNate.Web/Endpoints/YjsEndpoints.cs` resolves `documentName` the same way `/ticket` does:
- `page:<guid>` → the page itself
- `note:` / `napkin:` / `diagram:` → parent page via `Notes.PageId` (404 if unknown); authorizes `Page.View` there (notes inherit page permissions, D10); `noteId` + `documentName` are added to the event payload so consumers can tell note threads from page threads
- `pagemeta:` / `documents:` → 400 (no BlockNote threads there)
- `pageId` remains accepted for callers that already know the page

No client change needed — `commentAudit.ts`'s existing contract is now honoured.

## Tests

- `tests/AutoNate.Web.Tests/YjsCommentEventEndpointTests.cs` (9 cases): page doc → 204 + recorded `content.comment.created` with the pageId; note doc → parent pageId + noteId; legacy `pageId` body; unusable names → 400 with nothing published; missing both → 400; unknown note → 404. Red against the old handler (`Failed: 1`), green with the fix.
- `NotesTests.NotesPage_AddCommentOnRichTextNote_PostsCommentEventForNoteDocument`: drives BlockNote's real *Add comment* flow (type → select → toolbar → composer → Save) on a richtext note and asserts the `comment-event` POST for the `note:` document returns 204 with `eventType: created`.
- Full E2E: see the run summary in the final comment on archived-151.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Y5ie3qTEptr4MjYw5i6a5F

---

## archived-160 — test(e2e): link ResizeObserver allowlist entry to upstream Mantine issue/PR

`MERGED (merged 2026-08-31)` · nathanpond · opened 2026-08-31 · `chore/158-guard-upstream-link` → `master`

Refs archived-158 — comment-only: the allowlist rationale now points at mantinedev/mantine#9161 / #9162 so the entry can be removed when a release includes the fix.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Y5ie3qTEptr4MjYw5i6a5F

---

## archived-162 — fix(executor): isolate, time-limit and memory-cap the Python sandbox; close the host escape

`MERGED (merged 2026-08-31)` · nathanpond · opened 2026-08-31 · `fix/58-python-sandbox` → `master`

Closes archived-58
Closes archived-161
Closes archived-64

## What

The executor's Python path becomes a real sandbox:

- **One interpreter per request** in a single-use `worker_threads` Worker (`pythonWorker.ts`); warm pool + concurrency cap (`EXECUTOR_PY_WARM_WORKERS`=1, `EXECUTOR_PY_MAX_CONCURRENCY`=2). Nothing crosses authors (archived-58 ③).
- **Timeout that fires** (archived-58 ①): the main thread owns the deadline — reject at `timeoutMs`, SIGINT via Pyodide's interrupt buffer (`KeyboardInterrupt`), `worker.terminate()` after 250 ms grace for C-level loops. The NATS loop is never blocked.
- **`memoryMb` enforced** (archived-58 ②): `WebAssembly.Memory.prototype.grow` hook caps linear memory at baseline + `memoryMb` → Python `MemoryError`, interpreter stays usable. `EXECUTOR_PY_JS_HEAP_MB`=256 backstops the worker JS heap.
- **Host escape closed** (archived-161): `jsglobals: Object.create(null)`, `pyodide_js` unregistered + purged from `sys.modules`, `fetch`/`WebSocket` disabled in the worker, `_` (worker script host path) popped from `os.environ`. Left: Pyodide's in-memory FS and a fixed fake env.
- **archived-64**: inputs/config passed via `pyodide.globals.set` as JSON strings — no source splicing; quotes/backslashes round-trip.

`index.ts` prewarms the pool at start-up and drains NATS + terminates spares on SIGTERM/SIGINT. README + `docs/codebase/{Structure,Integrations,Architecture}.md` updated (Architecture still said the executor wasn't in compose).

## Evidence

- `services/executor`: `npm test` → **11/11** (`node --test`): round-trip incl. `"`/`\`, analyzer frame, entry-point + exception reporting, `while True: pass` stopped at 1500 ms with the runner serving afterwards, loop swallowing `KeyboardInterrupt` also stopped, `bytearray(256 MiB)` under `memoryMb=32` → `MemoryError` while 8 MiB succeeds, no global/entry-point leakage, all of `js.process` / `js.eval` / `js.fetch` / `pyodide_js` / `pyodide_js._api` / `run_js` / `open_url` / host FS rejected, env fixed, 3 concurrent requests.
- Compose image rebuilt (`make infra-ensure`) and smoked over NATS: `js OK 3ms`, `python OK 22ms` (warm worker), `py-timeout OK 2960ms "Python execution timed out after 2000ms."`, `py-escape OK` (AttributeError), `py-after OK` (next request served).
- Probe before the fix: `import js; js.process.cwd()` → the executor's real working directory.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Y5ie3qTEptr4MjYw5i6a5F

---

## archived-164 — fix(authz): fail closed by default and refuse to start on an open posture

`MERGED (merged 2026-08-31)` · nathanpond · opened 2026-08-31 · `fix/59-authz-fail-closed` → `master`

Closes archived-59

## What

Authorization can no longer fail open by omission.

- **Fail-closed defaults** (`AuthorizationOptions.cs`): `Enabled = true`, `Enforcement = "full"` (were `false` / `"off"`).
- **`AuthorizationOptionsValidator : IValidateOptions<AuthorizationOptions>`** registered with `ValidateOnStart()`, plus an eager `.Value` read right after `builder.Build()` so it fires **before any database or hosted-service work** rather than after a DB connect. Outside Development it refuses to start unless `Enabled == true && Enforcement == "full"`.
- **Unrecognised `Enforcement` is refused in every environment.** `Authorizer.cs:113` compares `Enforcement != AuthorizationEnforcement.Full` with ordinal equality, so `"Full"`, `"FULL"` or a typo read as *not full* and silently allowed every instance write. This was a second fail-open path the issue didn't cover.
- **Base `appsettings.json` ships an explicit `Authorization` section** (it previously had none at all) with the secure values and a comment block explaining each knob.
- **Startup warnings** outside Development for `DryRun=true` and `AssignSuperAdminToAllExistingUsers=true`.

## Two corrections to the issue's premises

- **"every newly created user is silently made SuperAdmin" is not accurate.** `SuperAdminBackfillSql` is gated by *both* the flag **and** a one-shot `auth_seed_state` key (`superadmin_backfill_v1`): it grants the role once, to users existing at that moment. Users created later get nothing. It is also the **only** startup path that grants SuperAdmin, so hard-failing on the flag (as suggested) would leave a greenfield install with **no** admin and, under `Enforcement=full`, unadministerable. Hence a loud warning instead of a refusal — and the README claim is corrected, with both traps documented (pointing at a DB that already holds other users; turning it off on a greenfield install).

## Evidence

Real host, `dotnet run`, bogus DB so nothing is touched — the authorization decision is reached before any DB work:

| environment + config | result |
|---|---|
| Production, `Authorization__Enabled=false` | **refused** — "Authorization:Enabled must be true outside Development…" |
| Production, `Enforcement=read-only` | **refused** — "…must be \"full\" outside Development…" |
| Production, **nothing configured** (new defaults) | passes authorization, proceeds (fails later on unrelated `Flowable:BaseUrl`) |
| Production, `Enabled=true` + `Enforcement=full` | passes authorization, proceeds |
| **Development**, `Enforcement=Full` (mis-cased) | **refused** — "…must be one of \"off\", \"read-only\", \"full\" (lower-case, exactly)…" |

**Tests**

- `AuthorizationOptionsValidatorTests` — 16 cases: the exact pre-fix defaults, every enforcement level, mis-cased/typo values in both environments, and an assertion that the validator is actually registered on the host.
- `AutoNate.Web.Tests` Authorization folder: **295/295**.
- Full `AutoNate.Web.Tests`: **1368 passed / 1 failed**. That one failure — `SubscriptionManagerTests.Disconnect_ClearsRegistryIndices` — is **pre-existing and unrelated**: a baseline run of `master` @ `f28a1c85` in the same checkout fails the *same* test with the same signature (1352/1, 16 s), it passes 3/3 in isolation, and it dies on a fixed 5 s WebSocket receive budget (`TaskCanceledException`), not an authorization decision. Filed as archived-163.
- The default flip is inert for the suite: all three direct `new AuthorizationOptions` constructions either set the fields explicitly or use `Enabled = false`, which short-circuits before `Enforcement` is read.
- Full E2E suite run on this branch (results in the closing comment on archived-59).

README + `docs/codebase/Architecture.md` updated.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Y5ie3qTEptr4MjYw5i6a5F

---

## archived-166 — fix(security): guard every outbound URL that carries user data or a credential

`MERGED (merged 2026-08-31)` · nathanpond · opened 2026-08-31 · `fix/60-61-outbound-url-guards` → `master`

Closes archived-60
Closes archived-61

Two shapes of one defect: a destination the caller controls, on a request carrying something worth stealing. Each gets the guard that actually fits it.

## archived-60 — REST data connector (SSRF)

`config.Url` went straight to `HttpClient` and the reply was parsed into rows the caller sees, so a connector pointed at `169.254.169.254` (or the Dapr sidecar, NATS monitoring, Flowable) read cloud credentials and internal-only APIs out through `POST /api/dataconnectors/{id}/preview`.

Calling arbitrary third-party APIs *is* the feature, so an allowlist is impossible — this gets `IOutboundUrlGuard` (`Services/Http/OutboundUrlGuard.cs`): scheme check, then every resolved address must be public, **before a socket opens**; `https` required outside Development. `IsBlockedAddress` moves off `WebFetchSkill` into `OutboundAddressRules` so the two guards can't drift — the skill keeps its public helper and delegates to it.

## archived-61 — provider credential exfiltration (wider than the issue said)

The issue named `IConnectionModelLister`. It is one of **five** consumers of the same connection-metadata `baseUrl`, and the least frequent:

| site | what it sends |
|---|---|
| `AnthropicChatProvider` | `x-api-key` on **every chat turn** |
| `OpenAIChatProvider` | `Authorization: Bearer` on **every chat turn** |
| `TavilyWebSearchProvider` | Tavily key on every search |
| `ConnectionModelLister` | the key (issue's named site) |
| `IAgentModelCatalogRefresher` | via the lister, on a background timer |

Guarding only the lister would have left the key flowing to an attacker-named host on every message — and a hostile base URL also feeds attacker-controlled text straight into the agent's context.

Here the legitimate hosts *are* a known short list, so this gets an allowlist (`IProviderBaseUrlPolicy`), which is strictly stronger than address classification — it can't be defeated by a DNS answer. Enforced at the three boundaries where untrusted metadata becomes a `Uri`: `ChatProviderResolver`, `WebSearchProviderResolver`, `ConnectionModelLister`. Built-ins `api.anthropic.com` / `api.openai.com` / `api.tavily.com`; `https` required; extend per kind via `ExternalConnections:AllowedProviderHosts` (host-only, `*.` wildcard).

**⚠️ Behaviour change:** a connection pointing at a custom base URL (Azure OpenAI, a corporate gateway, a self-hosted OpenAI-compatible model, Ollama) stops working until its host is allowlisted. That is precisely the capability being abused; the failure is an error naming the key to set. Documented in the README and `appsettings.json`.

## Found on the way

**archived-165** — `RestDataConnectorHandler.ParseConfig` deserializes case-sensitively while the SPA writes and documents camelCase, so UI-authored REST connectors never bind (`Url` stays empty). Proven with a standalone `System.Text.Json` probe. Filed rather than fixed here, because it is a functional bug and fixing it turns a currently-dead code path live — archived-60's guard belongs in place first. The connector tests therefore use the PascalCase shape that binds today, with a comment; they keep passing once archived-165 lands.

## Evidence

- `OutboundUrlGuardTests` — private/loopback/link-local/CGNAT/ULA/IPv4-mapped literals refused without touching DNS; hostnames resolving to private, and to *mixed* public+private, refused; non-http schemes; https-by-environment; DNS failure and empty answers; and a test pinning `WebFetchSkill.IsBlockedAddress` to the shared rules.
- `ProviderBaseUrlPolicyTests` — defaults per kind, official host, suffix confusion (`api.anthropic.com.attacker.example`), plain http, operator-configured host, `*.` wildcard (matches subdomains, not the bare suffix), per-kind isolation, unknown kind.
- `RestDataConnectorSsrfTests` — refused with **zero** HTTP calls (the stub handler counts and would have been invoked), guard runs on the `{lastFetchDate}`-interpolated URL, https-required outside Development, and a public https endpoint still fetches rows.
- Full `AutoNate.Web.Tests`: **1422 passed / 0 failed**. Full E2E: **141 passed / 0 failed / 2 skipped**.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Y5ie3qTEptr4MjYw5i6a5F

---

## archived-167 — fix(agent): authorize the two skills that read gated entities unguarded

`MERGED (merged 2026-08-31)` · nathanpond · opened 2026-08-31 · `fix/19-20-agent-skill-authorization` → `master`

Closes archived-19
Closes archived-20

Two agent skills read gated entities through stores that gate by nothing, so a user the REST API answers with **403** could read the same data by asking the chatbot.

| skill | what leaked | REST equivalent |
|---|---|---|
| `explain_workflow` / `find_workflow` | full BPMN of every workflow model — service-task endpoints, behaviour keys | `RequireKindPermission(WorkflowModel, View)` / `RequirePermission(..., "id")` |
| `list_system_issues` / `get_system_issue` | issues whose `FactsJson` carries verbatim exception text from `UnhandledExceptionRecorder` | `RequireKindPermission(SystemIssue, View)` |

Each skill now mirrors the gate its REST counterpart applies — kind-level for the list tools, per-instance for `explain_workflow` — running **before** the read. A denial is worded identically to a genuine miss, so `explain_workflow` can't be used to enumerate which workflow ids exist.

## The part that outlives this fix

archived-20 asked for the permanent version, and that is most of the value here. `AgentSkillAuthorizationTests` classifies **every** `IAgentSkill` in the assembly as one of:

- `Authorizer` — calls an authorizer itself
- `GatedStore` — reads via a store that applies `IAuthorizer` internally
- `ActorScopedStore` — `*ForActorAsync` / `*ForUserAsync` only
- `NoGatedData`

**A new skill fails the test until someone classifies it**, and a skill classified `Authorizer` fails if the call is later removed. Verified by deleting the guard from `ExplainWorkflowSkill` and watching four tests go red, including the gate.

The gate reads source rather than reflecting, deliberately: I wrote it as an IL scan first and it silently under-reported (it missed the notes skills, which authorize through `IContentAuthorizer`, not `IAuthorizer`). A guard test that can fail open is worse than none.

## Two findings from classifying the surface

- `LookupRecordsSkill` / `ManageRecordsSkill` mention `IAuthorizer` **only in comments** — my first sweep counted those and mis-classified them. They are in fact safe: `EfCoreRecordStore` takes `IAuthorizer` and folds `BuildRecordSqlFilterAsync` / `FilterQueryAsync` into every query. Verified in the store rather than taken on the comment's word.
- `LookupNotesSkill` / `ManageNotesSkill` authorize through `IContentAuthorizer`. Nothing wrong, but it is why the gate matches on call sites rather than one type name.

The other 12 unguarded skills were checked individually and are genuinely exempt — dashboards/notifications/saved-queries use actor-scoped stores, and AQL help, page-snapshot, web-fetch and web-search touch no gated entity.

## Evidence

- `AgentSkillAuthorizationTests` 11/11: denial paths use stores that **throw if touched**, so a refusal is proven to happen before the read, not as post-filtering; plus allowed-path tests, the denial-equals-miss test, and the two gate tests.
- Full `AutoNate.Web.Tests`: **1432 passed / 1 failed** — the failure is `SubscriptionManagerTests.Disconnect_ClearsRegistryIndices`, the known intermittent load flake tracked in archived-163 and previously reproduced on `master` with no branch changes.
- Full E2E: results in the closing comment.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Y5ie3qTEptr4MjYw5i6a5F

---

## archived-168 — fix(deps): patch the last SPA advisories via scoped overrides

`MERGED (merged 2026-08-31)` · nathanpond · opened 2026-08-31 · `fix/security-deps-37-38` → `master`

Closes archived-37
Closes archived-38

`npm audit` in `src/AutoNate.Spa` goes from **9 vulnerable packages to 0**.

## archived-37 — the nine SPA alerts

All nine were `nanoid` and `lodash-es` reached only through `@excalidraw/excalidraw`, which pins them itself — so `npm audit fix` was a **no-op** and neither could be bumped directly. Overrides are scoped per consumer rather than blanket, because the two `nanoid` instances need different lines:

| package | was | now | why |
|---|---|---|---|
| `lodash-es` | 4.17.21 | 4.18.1 | first line with the `_.template` code-injection + prototype-pollution fixes; minor bump within v4 |
| `nanoid` (excalidraw) | 3.3.3 | 3.3.18 | patch bump on the same line — and it now dedupes to the existing top-level copy |
| `nanoid` (mermaid-to-excalidraw) | 4.0.2 | 5.1.16 | the 4.x line has no fix; v5 keeps the main entry and the `nanoid()` signature and only drops `./async`, which nothing in the tree imports (verified) |

## archived-38 — already resolved

`services/hocuspocus` needed no change: the `ws` / `form-data` advisories were cleared by the earlier Hocuspocus 4.6 bump, and neither package is in its lockfile any more. Verified against both `npm audit` (0 vulnerable packages) and GitHub's alert list (0 alerts on that manifest).

## Coverage gap this exposed

The drawing note is the **only** surface that loads the Excalidraw bundle, and it had no test at all — so a dependency change underneath it was unverifiable. Adds `NotesPage_CreateDrawingNote_MountsExcalidrawCanvas`, which creates a Napkin note and asserts the canvas mounts; with `ConsoleErrorGuard` active, a module that fails to initialise fails the test.

## Evidence

`npm audit` → `{"info":0,"low":0,"moderate":0,"high":0,"critical":0,"total":0}`. ESLint 0 errors (13 warnings, at cap), `tsc -b` clean, `vite build` succeeds. Full E2E **142 passed / 0 failed / 2 skipped** — 142 rather than 141 because of the new spec.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Y5ie3qTEptr4MjYw5i6a5F

---

## archived-169 — fix(security): clear the remaining security backlog (authz, plugin grants, response hygiene, limits)

`MERGED (merged 2026-08-31)` · nathanpond · opened 2026-08-31 · `fix/21-22-23-endpoint-authz` → `master`

Closes archived-21
Closes archived-22
Closes archived-23
Closes archived-62
Closes archived-63
Closes archived-65
Closes archived-66
Closes archived-67
Closes archived-68

Clears the remaining security backlog. Two commits: endpoint authorization, then the lower-severity hardening.

## Endpoint authorization

- **archived-22** — `GET /api/code-transformers/{id}` returned the full Python/JS body, including for `IsUnsafe` rows, to any authenticated caller with a GUID, while the list endpoint beside it required `Transformer:List`. Now requires `View` on the row's own kind; a denial is `NotFound`.
- **archived-23** — create gated on `(Transformer, Run)` **whatever kind was requested**. `Run` is an execution grant, so letting someone execute a pipeline node also let them author the sandboxed code later runs execute — and because the kind was hard-coded, `analyzer:*` was never enforced at all. `Transformer`/`Analyzer` gain `Create`/`Edit`/`Delete` (there was no correct token before) and every route resolves the kind through `MapKindToEntityKind`.
- **archived-21** — `GET /api/content/locator/{n}` mapped a sequential long to (kind, id, ancestor chain) with no authorization, so a loop over the range enumerated the whole content tree and handed over the GUIDs to feed other endpoints. Every hit is now authorized (notes via their parent page, per D10) and a denial is byte-identical to an unknown locator.

⚠️ The route is `/api/content/locator/{n}`, **singular** — archived-21 quoted `/locators/`. Against that path my deny assertions passed *vacuously*, because a route miss is also a 404. Caught and corrected before merge.

## Hardening

- **archived-62** — every per-plugin role inherits `plg_readers`, which held SELECT on **all** tables plus default privileges for future ones, so any uploaded plugin could read password hashes, encrypted provider secrets, every other plugin's role password, and share-link token hashes. Reading app tables is a *documented* capability (`IPluginDataAccess` states it), so this revokes the credential tables rather than narrowing to an allowlist and breaking the contract. The revoke runs **last**: placed beside the GRANT it fires before several of those tables exist and `ALTER DEFAULT PRIVILEGES` hands them straight back — which my first attempt did, and the test caught.
- **archived-65** — datastore download replayed the uploader's `Content-Type`. Now shares page attachments' sanitiser (extracted so the two can't drift) plus `nosniff`.
- **archived-66** — NATS published 4222/8222 on all interfaces with no auth, beside Dapr endpoints already on loopback. Bound to `127.0.0.1`; containers still reach `nats:4222`.
- **archived-67** — the 1 GiB body limit was global. Now 64 MB globally with the large ceiling on the datastore upload route only, and the XLSX transformer refuses >64 MB cleanly rather than as an OOM on a worker thread.
- **archived-68** — connector and datastore previews folded raw exception text (internal hostnames, connection-string fragments, Postgres `MessageText`) into responses. Detail goes to the log against a correlation id; callers get the id.

## archived-63 — exploit claim corrected

The issue's attack (forge a tiny declared size, expand to gigabytes) **does not work on .NET**: `ZipArchive` truncates each entry stream at the declared size. Measured — an 8 MiB entry forged to declare 10 bytes yields exactly 10 bytes. Full evidence posted on archived-63.

`PluginZipExtractor` still replaces both `ExtractToDirectory` calls, so the bound rests on **bytes actually written** rather than that runtime detail, and entry paths are re-checked by the code that creates files. A test pins the truncation behaviour so the suite says something if a future runtime stops doing it.

## Evidence

- `CodeTransformerEnforcementTests`, `ContentLocatorEnforcementTests`, `PluginReaderGrantTests`, `PluginZipExtractorTests` — **all four guards red-checked** by reverting them and confirming the tests fail (locator: 2 fail; plg_readers: 3 credential tables become readable).
- Full `AutoNate.Web.Tests`: **1452 passed / 1 failed** — the failure is `SubscriptionManagerTests.Disconnect_ClearsRegistryIndices`, archived-163's known intermittent flake, previously reproduced on `master` unchanged.
- Full E2E: **142 passed / 0 failed / 2 skipped**.

Three failures surfaced along the way and were each run down rather than retried:
- `PluginDataIsolationTests` — genuinely broken by archived-62, because it used `public.plugins` as its "plugin can read public" probe and that table holds every plugin's role password. Probe moved to an ordinary table; the test now also asserts the credential tables are denied.
- `DataConnectors_PreviewModal_OpensAndShowsConnectorReply` — a Playwright strict-mode violation, not a wrong outcome: archived-68's new body text also matches the spec's own regex, so the error state resolved to two elements.
- `ApiNotFoundGuardTests` + `DataStoreListFilteringTests` — **environmental**: the E2E fixture empties `src/AutoNate.Web/wwwroot`, and both need the built SPA. They fail in isolation too, so they read as real breakage. Details and the CI consequence are on archived-163.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Y5ie3qTEptr4MjYw5i6a5F

---

## archived-170 — chore(executor): make the tsconfig TypeScript 7 ready

`MERGED (merged 2026-08-31)` · nathanpond · opened 2026-08-31 · `chore/ts7-sidecar-groundwork` → `master`

Groundwork so archived-99 (typescript 5.9.3 → 7.0.2 in `services/executor`) becomes a plain version bump.

TypeScript 7 rejects the current tsconfig with **TS5011** (`rootDir` must be explicit), and once that's satisfied it fails to resolve `@types/node` globals — 13 errors across `index.ts`, `healthcheck.ts` and `pythonRunner.ts` (`process`, `NodeJS`). An explicit `rootDir` plus `types: ["node"]` fixes both.

Both options are **inert on the pinned 5.9.3**, so this lands independently and de-risks the bump: verified by building and running the executor's full suite (**11/11**) on 5.9.3 with the new tsconfig, and by compiling a copy against **typescript@7.0.2** (clean with, 13 errors without).

`services/hocuspocus` needs no change — it already sets `rootDir` and compiles clean under 7.0.2 as-is, which is worth knowing before archived-106.

Honest caveat: I could not pin down *why* `@types/node` resolves for hocuspocus but not the executor under TS 7 (not `incremental`, not a stale tsbuildinfo, both have the package). The fix is verified in both directions regardless.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Y5ie3qTEptr4MjYw5i6a5F

---

## archived-171 — fix: twenty bugs — leaks, unbounded work, config drift, and two analyzers back on

`MERGED (merged 2026-08-31)` · nathanpond · opened 2026-08-31 · `fix/small-defects-batch1` → `master`

Closes archived-18
Closes archived-30
Closes archived-31
Closes archived-41
Closes archived-50
Closes archived-72
Closes archived-73
Closes archived-74
Closes archived-75
Closes archived-76
Closes archived-77
Closes archived-78
Closes archived-113
Closes archived-116
Closes archived-118
Closes archived-121
Closes archived-122
Closes archived-163
Closes archived-165

Twenty bugs, grouped by what they actually are.

## Resource leaks and unbounded work

- **archived-72** — the repeated-auth-failure detector keyed a singleton map by the *attacker-supplied* username from the unauthenticated login endpoint and never evicted, so credential stuffing grew the heap indefinitely. Sweeps emptied windows, plus a hard ceiling. **Red-checked.**
- **archived-77** — `DeleteAllWorkflowExecutionsAsync` always refetched page 0 and exited only on an empty page, so one undeletable instance meant refetching forever — an unbounded HTTP hammer from inside a live admin request. Stops when a pass deletes nothing, plus a time budget.
- **archived-73** — the Dapr watchdog's 45 s timeout never killed the child, so a hung restart script was orphaned and another spawned every 2 minutes. Now kills the process tree, and drains both pipes concurrently (a child writing past the 64 KB buffer deadlocked).
- **archived-78** — `NatsConnectionProvider` read its cached connection outside the lock without `Volatile` and never re-validated it, so a terminally-closed handle was reused until process restart.
- **archived-74 / archived-75** — Hocuspocus `pg.Pool` had no `'error'` listener (idle-client failure becomes an uncaughtException), and neither cross-service fetch had a timeout (undici's 300 s default).

## Wrong behaviour

- **archived-165** — the built-in REST connector **could not be configured through its own admin page**: the SPA writes camelCase, `System.Text.Json` binds case-sensitively, so `Url` was always empty. The SSRF tests move to the camelCase shape they should have used all along.
- **archived-113** — `ensure-nats-stream.sh` ran `stream edit --subjects` on every invocation, re-narrowing the stream and dropping record/application/content publishes on every `make infra-ensure`.
- **archived-116** — two literal NUL bytes made grep treat `WorkflowStudio.tsx` as binary. They were **deliberate key delimiters**, so stripping them would have silently introduced collisions; replaced with the escape sequence instead, byte-identical at runtime.
- **archived-18** — every tab and history entry read "AutoNate" (WCAG 2.4.2 / 508 §502). Titles come from `APP_ROUTES` centrally, with a path-derived fallback for dynamic pages — a gap the new E2E spec caught rather than my assuming it was covered.

## Enforcement instead of discipline

- **archived-41** — CA2016 and S108 back to `warning`. The issue predicted "the codebase is clean"; **19 sites fired**, and the first CA2016 hits were in handlers archived-76 had just given a token to. All six were genuine unforwarded tokens; the thirteen S108 blocks now say why they are empty. Both rules at zero.
- **archived-118** — 411 to **164** warnings. The 234 JSX entities were escaped from ESLint's own line/column positions rather than by pattern-matching the source (234 fixed, 0 skipped); 13 unused directives removed; `--report-unused-disable-directives` added so the next one fails lint.
- **archived-163** — the 5 s WebSocket budget failed on most full-suite runs while passing in isolation. It is a hang guard, not a latency assertion: now 30 s and env-overridable.

## Dead code and config drift

**archived-30**, **archived-31**, **archived-50** (re-verified by trial-delete plus `tsc -b --force`, not taken on the issues' word), **archived-121** (`Flowable:BaseAddress` bound to nothing), **archived-122** (a feature flag nothing read).

## Verification

Full `AutoNate.Web.Tests` **1458 / 0** — including `SubscriptionManagerTests`, which archived-163 fixes. Full E2E **142 / 0 / 2** after the dynamic-page title gap was closed.

Worth noting for whoever runs these: Web.Tests must run **before** E2E, which empties `wwwroot` (see archived-163's thread).

## Not closed here

**archived-119** — `IFlowableReadThrough` is referenced by `ProjectionFrameworkPhase2Tests` and is the intended landing point for archived-52, so deleting it would be churn rather than a fix. Left for archived-52.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Y5ie3qTEptr4MjYw5i6a5F

---

## archived-174 — fix: health probe, inert grants, and every E2E spec behind the console guard

`MERGED (merged 2026-08-31)` · nathanpond · opened 2026-08-31 · `fix/bugs-batch2` → `master`

Closes archived-24
Closes archived-88
Closes archived-89
Closes archived-93
Closes archived-115

Five more bugs, plus two product defects promoted out of prose into tracked issues.

## Monitoring gap

**archived-115** — every notes, pages, documents and diagram load rides on the Hocuspocus sidecar, and nothing probed it: a collab outage showed **fully green** health while every Y.Doc load failed in the SPA. That is precisely the class of failure the 5 s health poll exists to surface. Adds a TCP reachability probe on `YjsServer:HocuspocusWsUrl` with the same 3 s budget the other probes use.

TCP rather than a WebSocket handshake on purpose: Hocuspocus rejects an unauthenticated upgrade *by design*, so a handshake would need a minted ticket and would report "down" for what is actually an authorization result.

## Grants that did nothing

**archived-24** — three things `/api/admin/registry` advertised had no effect:

- **Pipeline cancel was gated on `Run`**, so an admin who granted `pipeline:cancel` to an on-call operator watched them 403, and the only working grant was the one that also lets them *start* runs. Now honours `Cancel`. ⚠️ Deployments relying on run-implies-cancel need the cancel grant added — that is the fix, but it is a real migration step.
- **`Dataset:schedule` / `Pipeline:schedule` removed** rather than wired: the cron lives in the same row as the rest of the definition and is edited through the same endpoint, so `Edit` is genuinely the gate and a separate grant has nothing to attach to.
- **The `PipelineRun` kind removed** — no endpoint ever authorized against it (runs are reached through their pipeline), so its registry entry and selector compiler advertised an access-control surface that did not exist.

## Test-suite integrity

**archived-93 — the issue named the wrong file.** `PipelinesAdminTests` uses `NewSignedInAsAdminAsync` fourteen times and builds no context of its own. The real gap was larger: **six other spec files built 19 hand-rolled contexts, every one unguarded**, because `NewSignedInAsAdminAsync` was the only helper installing `ConsoleErrorGuard` and those specs needed a limited user or an anonymous visitor.

`E2ETestBase` gains `NewSignedInAsAsync(username, password)` and `NewAnonymousSessionAsync`; AgentSidebar, WorkflowOverride, Notifications, AuthShell, PermissionGating and Login all move onto guarded sessions. Permission-denial journeys are the worst place to lose this — the page is *supposed* to look empty, so an exception there is invisible. All 23 converted specs pass with the guard on, so nothing was hiding behind it. One raw context stays in AuthShellTests deliberately: API-only, no page, no console to guard.

**archived-89** — a fixed 3 s sleep stood in for "Hocuspocus has persisted the Y.Doc". There is no server-side signal to poll for a *document* body: persistence goes sidecar → `yjs_documents` with no API over it, and the content-version bump the webhook does is page-specific, so the version does not move here. I measured that — **my first two poll designs were wrong, one of them passing vacuously** because a fresh document starts at version 2, not 1. The spec now retries the real assertion instead: reload, look for the text, reload again if absent. Disconnecting on reload is itself what flushes the sidecar, so a too-early attempt costs a round trip rather than the edit.

**archived-88** — two `[Fact(Skip)]` specs described product defects in prose with no issue behind them, which makes a skipped test a permanently silent bug. Filed **archived-172** (Appearance save silently reverts on reload — data-loss-shaped) and **archived-173** (DOCX import never finalises parsed content). Both Skip messages now name their issue and say the spec is that issue's acceptance test.

## Verification

Full `AutoNate.Web.Tests` **1458 / 0**. Full E2E **143 / 0 / 2**.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Y5ie3qTEptr4MjYw5i6a5F

---

## archived-175 — fix(a11y): contrast, colour-only meaning, route focus, and the silent login error

`MERGED (merged 2026-08-31)` · nathanpond · opened 2026-08-31 · `fix/bugs-batch3-a11y` → `master`

Closes archived-7
Closes archived-15
Closes archived-16
Closes archived-17

Four accessibility defects, all in the SPA.

## archived-7 — the default sidebar heading colour was unreadable

`sidebarSectionColor` shipped as `#adb5bd` on white: **2.07:1**, against a 4.5:1 requirement that applies because 0.78rem bold uppercase is not WCAG "large text". Every SITE / SECURITY group heading — the thing that makes a 30-item admin nav navigable — was effectively invisible to low-vision users.

Now `#5c636a` (**6.09:1**), still muted enough to read as a heading rather than body text. Ratios computed with the WCAG relative-luminance formula, not eyeballed.

The pair is also added to `CONTRAST_CHECKS`, which is *why* this shipped: the admin editor warns on nine pairs and this was not one of them, so nothing ever flagged the default.

## archived-16 — archived rows meant something only if you could see colour

`.row-archived td { color: dimmed }` was the entire cue. A screen-reader user got no signal at all; a colour-deficient user saw a lightness shift with no reason to read it as "archived" (WCAG 1.4.1).

A shared `<ArchivedBadge>` now carries the meaning **in text**, in the primary cell of all three lists (records, record types, relationship types). The dimming stays as the supporting visual, so the state is conveyed through three independent channels rather than one.

## archived-15 — navigation announced nothing

In a server-rendered app a navigation resets focus to the document; in an SPA it does not, so focus stayed on whichever nav link had just been activated. A screen reader announced nothing on route change, and Tab resumed in the header rather than in the page the user had just asked for (WCAG 2.4.3).

`useRouteFocus` moves focus to the `#content` wrapper the skip link already targets — it carries `tabIndex={-1}` for exactly this purpose. Skipped on first paint, where the browser's own initial focus is correct and stealing it would also fight any landing-form autofocus.

## archived-17 — a failed sign-in was silent

The error rendered without `role="alert"`, so it was never announced: the user was left on a form that appeared to have done nothing (WCAG 3.3.1 / 4.1.3). Both inputs also carried `autoFocus`, which drops a screen-reader user mid-form, past the brand and heading that say *which site* they are signing in to. The form is two fields — Tab reaches them immediately — so the autofocus is removed rather than made conditional.

## Verification

Full E2E **143 / 0 / 2**. `tsc -b` and `vite build` clean. Lint warnings 164 → **162** (the two `no-autofocus` warnings), ratcheted in `package.json` and the docs per the convention.

No `AutoNate.Web.Tests` run for this one: the batch is SPA and docs only, and the backend suite does not exercise the SPA. E2E is the gate that matters here.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Y5ie3qTEptr4MjYw5i6a5F

---

## archived-176 — fix: silent-failure defects — outbox timeout, leaked plugin ALCs, executor lifecycle, unauthorable grants

`MERGED (merged 2026-08-31)` · nathanpond · opened 2026-08-31 · `fix/bugs-batch4` → `master`

Closes archived-25
Closes archived-69
Closes archived-70
Closes archived-71

Four defects that all end the same way: something breaks quietly and stays broken until a human happens to notice.

## archived-71 — an untimed publish inside an open transaction

`AuditOutboxDispatcher` resolved the **unnamed** `HttpClient` (100 s default) and published up to a 100-row batch serially — all inside an open Postgres transaction holding `FOR UPDATE` locks on those rows.

Against a Dapr sidecar that accepts TCP but stalls (the exact state `DaprStreamingSubscriber` documents), that transaction could stay open for hours: `idle in transaction`, autovacuum's xmin horizon pinned **database-wide**, bloat everywhere — with nothing but one `LogError` per row to say so.

Now a named client with a 5 s budget: far beyond a healthy local publish, and it bounds the whole batch to minutes.

## archived-70 — a leaked assembly load context per failed enable

`PluginRuntime` builds a **collectible** ALC before validating the plugin, and only the success path hands it to `LoadedPlugin`. All four early returns and the catch dropped the reference without unloading — leaking the ALC, its assembly and its resolver for the process lifetime, and keeping the `.dll` memory-mapped. That last part is why re-uploading a fixed build over the same folder failed.

It compounds rather than being a one-off, because `PluginEnableFailureDetector` actively prompts the admin to retry. A `finally` now unloads unless the ALC was retained, and a failure to unload is logged rather than masking the original error.

## archived-69 — the executor could stop serving without exiting

It connected with nats.js defaults, so after ten failed reconnects (~20 s) the connection closed for good: the subscription iterator completed *normally*, `main()` resolved, and the process either exited 0 or idled with an empty loop — while every code-node pipeline failed with the generic 30 s timeout.

Now it reconnects indefinitely, exits non-zero if the connection closes anyway **or if the loop ever ends**, and installs `unhandledRejection` / `uncaughtException` handlers that do the same. Compose already restarts it, so exiting is the correct signal rather than lingering as a healthy-looking process with no subscription.

## archived-25 — grants the runtime honoured but nobody could author

`Document` and `Folder` are enforced on 22 route registrations and honoured by `ContentAuthorizer`'s `/document/…` and `/folder/…` selectors, but neither appeared in `CoreEntityTypes` — and `/api/admin/registry`, which drives the Grants admin picker, is built from that list.

So the only way to author these grants was `ContentPermissionOverrideEndpoints`, which carried its **own hardcoded action lists**: a second source of truth free to drift from the enforcement it was meant to describe. The registry now uses those same lists, so the two agree by construction — `Comment` document-only (folders hold no discussion), `Create` folder-only (a folder is where a document gets created).

`EntityRegistryTests` asserted an exact count of 17, which is what caught this; it now expects 19 and names both kinds, so the next addition has to be deliberate too.

## Verification

Full `AutoNate.Web.Tests` **1458 / 0**, full E2E **143 / 0 / 2**, executor suite **11 / 11**.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Y5ie3qTEptr4MjYw5i6a5F

---

## archived-177 — fix: enforce Record:Assign where it is actually used, retire dead surface, explain every suppression

`MERGED (merged 2026-08-31)` · nathanpond · opened 2026-08-31 · `fix/bugs-batch5` → `master`

Closes archived-32
Closes archived-33
Closes archived-45
Closes archived-46

## archived-45 — a permission that could not be exercised

`PUT /api/records/{id}/assignees` carries `RequirePermission(Record, Assign)` and has **no caller outside its own test**. The SPA changes assignees through `PATCH /api/records/{id}`, which accepts `assigneeIds` and was gated on `Edit` alone — so `Record:Assign` could be granted or denied with no observable effect for any real user.

The fix is server-side rather than pointing the SPA at the other route: the PATCH handler now additionally requires `Assign` whenever the body actually carries assignees. That way the permission means the same thing however the change arrives, instead of depending on which route a client happens to pick.

Tests cover all three cases — assignees without the grant → 403, with it → 200, and a non-assignee edit still needing only `Edit`, so this charges `Assign` for assignees rather than making `Edit` require it. **Red-checked** by removing the gate.

## archived-46 — an orphaned route

`GET /api/forms/{id}/versions/{versionNumber}` had no caller anywhere in the SPA, tests, plugins or docs, and `IFormStore.GetVersionAsync` existed only to serve it. An authenticated, untested route is a liability rather than an option, and the restore flow does not use it — deleted. Re-adding it *with a test* when the version-diff UI is built is cheap.

## archived-33 — filenames naming delivery phases

`ProjectionFrameworkPhase2Tests`, `ProjectionFrameworkPhase3Tests` and `Phase7DocumentImportTests` named phases that resolve to nothing now that `docs/plans` is historical, so a reader had to open all three to learn what was covered. Renamed for behaviour: `ProjectionVariableHistoryAndRetentionTests`, `ProjectionColdTierArchiveTests`, `DocumentImportTests`.

## archived-32 — suppressions that did not say why

Eleven `eslint-disable` directives carried no reason, which makes a deliberate suppression indistinguishable from a stale-closure bug someone silenced. Each now says why it is safe, **derived from the surrounding code rather than boilerplate** — several already had the reason in a comment above, just not where a linter could see it.

`eslint-comments/require-description` is on as an **error**, so the next bare directive fails lint. Verified by adding one and watching it fail.

## Verification

Full `AutoNate.Web.Tests` **1461 / 0**. Full E2E **143 / 0 / 2**.

One note on getting there: the first full run showed `NotesQueryEndpointTests.FromNotes_OnEmpty_ReturnsSchemaColumns` failing, and my isolation re-run appeared to confirm it — but that re-run was polluted, because it ran straight after E2E, which deletes the static-web-asset manifests the test host needs. After a rebuild it passes 8/8 on this branch, `master` passed throughout, and the clean full run above is green. Added to the notes on archived-163 so the next person does not chase it the way I did.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Y5ie3qTEptr4MjYw5i6a5F

---

## archived-178 — fix(a11y): make DataTable and Notes page rows keyboard-operable (archived-10, archived-12)

`MERGED (merged 2026-09-01)` · nathanpond · opened 2026-08-31 · `fix/bugs-batch6-keyboard` → `master`

Closes archived-10
Closes archived-12

Two keyboard-accessibility defects where the primary task of a screen was simply unreachable without a mouse (WCAG 2.1.1 / 4.1.2, 508 §502).

## archived-12 — DataTable rows

`DataTable` forwarded `onRowClick` straight to mantine-datatable, which renders a bare `<tr onClick>`: no `tabIndex`, no name, no key handler. Any table whose only way into a row is the row itself was mouse-only — and **Notifications is exactly that**, with no link or button in any cell, so a keyboard or AT user could not open a single notification. `RecordList` survived only because its Key column happens to contain a `<Link>`.

The wrapper also accepted `getRowAriaLabel` and threw it away, with a comment claiming mantine-datatable exposes no per-row aria props. It does — `customRowAttributes` — so rows are now focusable, named by that same callback (Notifications was already passing one), and answer Enter and Space.

**Deliberately not `role="button"`.** A `<tr>` has to keep its row role or the table stops being exposed as a table, and browsers will not surface a row as a button anyway. Focusable + named + Enter/Space is what makes it operable while remaining a row. The key handler ignores events that bubbled from a control inside the row, so Space still types and Enter still submits where it should.

## archived-10 — Notes explorer page rows

Page rows were `<div onClick>` with no `tabIndex`, role or key handler and no alternate link, so a keyboard-only user could reach Notes and then not open a single page — the sharpest of these, since opening a page *is* the module.

The fix is the pattern the notebook row a few hundred lines above already uses, comment and all: the row stays a `<div>` because it contains the "+" and kebab buttons and nested `<button>`s are invalid HTML, so `role="button"` plus `tabIndex` restores focus and Enter/Space. Inner buttons keep their own keyboard behaviour.

Worth noting: archived-10 also listed notebook (`:563`) and cabinet (`:816`) rows, but those line numbers now point at control spans — that row markup already carries role/tabIndex/onKeyDown, so the page row was the remaining gap.

## Verified, not asserted

Both fixes have E2E specs that actually drive the keyboard: focus the row, press Enter, assert the destination. That mattered — the Notifications spec failed twice against role-based locators before I dumped the rendered row and found why: a `<tr>` computes its accessible name from its cells, so `aria-label` on the row is not exposed as its name. The spec locates by attribute instead, which here is the feature under test rather than a lapse from archived-92's guidance.

## Verification

Full `AutoNate.Web.Tests` **1461 / 0**. Full E2E **144 / 0 / 2** (146 total — the two new specs). Lint warnings 162 → **160**, ratcheted in `package.json` and the docs, because the Notes fix retired two `jsx-a11y` warnings for real rather than suppressing them.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Y5ie3qTEptr4MjYw5i6a5F

---

## archived-179 — fix(a11y): finish the keyboard cluster; mount the real security admin; make DOCX import commit (archived-8, archived-9, archived-11, archived-13, archived-42, archived-172, archived-173)

`MERGED (merged 2026-09-01)` · nathanpond · opened 2026-09-01 · `fix/bugs-batch7-keyboard` → `master`

Closes archived-8
Closes archived-9
Closes archived-11
Closes archived-13
Closes archived-42
Closes archived-172
Closes archived-173

Seven issues: the rest of the keyboard-accessibility cluster, plus three "the feature is there but does nothing" defects.

---

# Keyboard accessibility

## archived-11 — MenuTreeEditor rows

Both row branches put `onClick` on a bare `<li>`, and every nested control `stopPropagation`'d, so no child could select the row. A keyboard admin could expand, hide, edit and delete rows but never select one.

The row label is now a real `UnstyledButton` (the separator branch gets one around its badge, since it has no label), which brings focus, Enter/Space and an accessible name for free, plus `aria-current` so selection is announced rather than conveyed by background colour alone. The `<li>` keeps its `onClick` so whole-row clicking still works.

**Correction to the issue:** it says selection "opens the detail editor", but `selectedId` only drives the row highlight — editing is the pen icon, which was already reachable. The defect is real; the consequence is narrower.

## archived-13 — AgentSidebar focus contract

Opening the assistant left focus on the header button, so reaching the composer meant tabbing blind through the page; closing orphaned focus on `<body>`; Escape did nothing.

Now focus moves to the composer on open, Escape closes from inside the panel, and focus returns to whatever opened it — captured from `document.activeElement` rather than found by selector, so it holds for the ⌘K palette too. Skipped on first paint, since `isOpen` is restored from localStorage and the panel can already be open on load.

**Deliberately not a FocusTrap**, which is what the issue suggested. In push / under-header modes this panel is non-modal and the page behind it stays usable, so trapping Tab would be a keyboard trap (WCAG 2.1.2) rather than a fix.

## archived-8 / archived-9 — the Notes dialogs

Every Notes dialog was a raw `<div onClick={onClose}>` overlay wrapping a `<div onClick={stopPropagation}>` panel. No role, so a screen reader announced nothing; no focus trap, so Tab walked out into the page behind; no focus return, so closing dropped the user on `<body>`. Creating, renaming, moving and deleting notes were all mouse-only. Fields compounded it: a presentational `<div>` label beside an id-less `<input>` reads as "edit, blank".

A new shared `NotesModal` wraps Mantine's compound Modal — role, `aria-modal`, `aria-labelledby`, focus trap, Escape, scroll lock — while the compound parts keep the module's own chrome, which is styled from `notesTheme` to match the design prototype rather than the global Mantine theme. Applied to all eleven dialogs. Labelled fields became Mantine `TextInput`, which wires a real `<label htmlFor>`.

**Mantine's own `returnFocus` does nothing here**, which is worth knowing beyond this PR: it runs through `useFocusReturn`, whose effect is a `useDidUpdate` that skips the first render. It only works for a modal mounted once and toggled via `opened`. These dialogs are conditionally rendered, so they mount with `opened` already true and unmount on close — neither the capture nor the restore branch ever runs. The shell captures the opener itself. **The E2E assertion on focus return is what surfaced this; inspection would not have.** The same pattern appears in `components/ConfirmModal.tsx`.

**Two corrections to the issues:**
- archived-8 lists twelve dialogs including `EditorPane:783`. That one is not a dialog — it is the ellipsis dropdown, `position: absolute` at `zIndex: 60`, whose items are already real `<button>`s and which already handles Escape and outside-click. Converting it to a centered modal would have been wrong, so it is untouched. It does lack `role="menu"` and arrow-key navigation — a smaller, separate gap.
- `FaIconPicker`, shared by the cabinet and notebook dialogs, had the same defects and was on neither issue: an unlabelled search box and icon buttons whose selected state was border colour alone. Both fixed.

Lint warnings **162 → 110**: 52 `jsx-a11y` warnings retired by the fix itself, not suppressed. Cap ratcheted in `package.json` and the docs.

Also: ConfirmDialog's document-level Enter handler is gone. With focus now starting on the confirm button it would have fired `onConfirm` twice — and it had always confirmed even when focus sat on Cancel.

---

# Functional defects

Three defects that all presented as "the feature is there but does nothing", plus the locator cleanup they forced.

## archived-42 — the shipped security admin was unreachable

The seeded Site Configuration → Security menu points at the `configSecurity*` template keys, and those keys rendered "coming soon" stubs while the real user/group/role/grant/explain admin shipped under separate `manageUsers` / `adminRoles` / … keys. The feature existed and could not be reached from the one place the seed sends an admin.

The seed was never wrong: its own `page_templates` rows describe these keys as *"User management mounted inside Site Config"* — exactly the intent. Only the SPA registry disagreed. The five keys now resolve to the same components their siblings do, and the five dead stubs are deleted.

**Not fixed here:** `configFormMappings` is also a stub, but that one is honest — there is no form-mappings page anywhere in the SPA, so pointing the key somewhere real is a feature, not a wiring fix.

The E2E theory asserts content only the real page renders ("All groups", "Add grant", …) *and* that the stub sentence is absent, because every stub rendered an h1 too — a heading-shape assertion would not have caught this. All five red-check.

## archived-173 — DOCX import silently discarded the upload

Import navigated to `?import=1` and stayed there: parsed content never reached `body_jsonb`, the stash was never discarded, and the document reloaded empty.

The finalize hung off docx-editor's `onChange`, on the assumption that the OOXML parse pass surfaces as change events. **It does not.** Instrumented a real import: `onEditorViewReady` fires twice, `onChange` never fires at all, so the 500 ms debounce never armed and `onImportFinalized` was unreachable.

Finalize now watches the editor's document settle — poll every 250 ms, require two consecutive stable non-empty reads, then serialize. Deliberately dumber than any event, so it does not care which lifecycle hooks the library chooses to emit. Waiting for *stable* rather than firing on the first non-empty read matters because a long document arrives across several transactions.

On timeout it deliberately does **not** finalize: committing an empty body would clear `?import=1` and destroy the server-side stash, which at that point holds the only copy of the upload. Leaving import mode in place means a reload retries the parse.

## archived-172 — not a bug

The report was that Appearance accepts a new Site name, reports success, and reverts to the default on reload. It does not. Its skipped spec reloaded on the line after the Save click, and the navigation aborted the in-flight PATCH.

Measured rather than argued: `PATCH /api/admin/appearance` → 200, the success alert renders, `GET` returns the new `siteName`, and after a reload the input shows the saved value. The spec now waits for the page's own "Appearance settings saved." alert — the signal a user waits for too — which also makes it assert that saving *reports* success. Green on three consecutive runs.

## Locator cleanup this forced

Batch 7's named menu-row button shifted `row.Locator("button").Nth(4)` and broke two passing specs. The underlying problem was that the visibility / edit / delete icons had **no accessible name at all** — a Mantine Tooltip sets `aria-describedby`, not the name — so the specs had nothing to ask for but a position. Naming them fixes the a11y gap and retires 7 positional locators, which is what archived-92 is about.

## Verification

Full E2E **153 / 0 / 0 skipped** — both previously-skipped acceptance specs now run and pass. Full `AutoNate.Web.Tests` **1461 / 0**, `tsc -b`, `vite build` and lint all clean.

One note on getting there: the first backend run showed `NotesQueryEndpointTests.FromNotes_OnEmpty_ReturnsSchemaColumns` failing. That is the known artifact recorded on archived-163 — the E2E fixture empties `AutoNate.Web/wwwroot` and deletes the static-web-asset manifests, so a backend run before rebuilding `AutoNate.Web` is unreliable. Rebuilt, then 8/8 in isolation and green in the full re-run above.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Y5ie3qTEptr4MjYw5i6a5F

---

## archived-180 — fix: retire writes nothing reads; wire up the paths that led nowhere (archived-43, archived-44, archived-47, archived-14, archived-48)

`CLOSED` · nathanpond · opened 2026-09-01 · `fix/bugs-batch9-backend` → `fix/bugs-batch7-keyboard`

Closes archived-43
Closes archived-44
Closes archived-47
Closes archived-14
Closes archived-48

Five defects sharing a shape: something is written that nothing reads, or offered that leads nowhere.

> Stacked on archived-179 — review that one first; the diff below is the last five commits.

## archived-43 — a write with no possible reader

`AuthCacheBumper.BumpAsync` issued an `UPDATE` on every grant, role, group and role-assignment mutation — **17 call sites** — for a process-wide auth cache that was never built. Nothing ever `SELECT`ed the version.

It could not have had a consumer either: `Authorizer` is registered **scoped**, so its grant cache and SQL-filter cache live and die inside a single request and cannot go stale across a mutation. The bump was a per-mutation round-trip buying nothing — and its comment ("caches built around the version number become stale automatically") would have told the next person that invalidation already worked.

Deleted: the class, all 17 call sites, the DI registration, the test helpers, and the table itself. Leaving an orphan table behind just relocates the puzzle.

## archived-44 — a dead-letter table only psql could reach

`AuditOutboxDeadLetterParkRemediator` moves an abandoned `audit_outbox` row into `audit_outbox_dead_letters` so that, in its own words, *"forensics is still possible"*. Nothing read the table — no `SELECT`, no route, no UI — so the self-healing story ended in a black hole, and the two indexes served nothing.

`GET /api/system-issues/dead-letters` lists them; `POST …/{id}/replay` puts one back. Replay deletes the dead letter and inserts the outbox row **in a single statement**, so a double-click cannot enqueue the same event twice; a second replay is a 404, not a duplicate. `attempt_count` resets to 0 — this is a fresh delivery, not a continuation of the run that exhausted its retries.

Both gated on `SystemIssue:Remediate` rather than `View`, **including the read**: these rows carry the raw payload of the dropped event, which the ordinary issue list never exposes, so the people who should see them are the ones who can act on them. The panel treats a 403 as "hide the panel" rather than an error, so a View-only operator still gets the page they are entitled to.

## archived-47 — the documented recovery step had no button

`reset-watermark` is documented in `docs/projection-framework/operations.md` as *the* recovery step for a corrupted or retention-truncated cache, but was reachable only by curl: no API client function, no button, no test — while its siblings pause/resume/rebuild had all three.

The Feeds column now lists each feed with its own confirm-guarded **Reset watermark** action rather than collapsing them into a hover-only count: the reset acts on one feed, so an operator has to be able to say which.

## archived-14 — button text picked by the wrong maths

`badgeTextColor` thresholded **YIQ brightness** at 160 — a video-encoding measure with no defined relationship to contrast ratio. Its result becomes `--mantine-primary-color-contrast`, so it was the text colour of every filled primary button in the app, not just status pills.

It now returns whichever of black/white actually measures higher under WCAG. Not theoretical: `#00acac` went white at **2.80:1** and is now black at **6.74:1**; `#348fe2` went 3.40:1 → 5.55:1. The default `#008080` keeps white at 4.77:1, so the shipped theme is unchanged.

The WCAG primitives lived privately in `siteAppearance.ts` while `statusAppearance.ts` carried its own heuristic — and `siteAppearance` imports `badgeTextColor` from `statusAppearance`, so the latter could not import the real math back. Both now depend on a shared `lib/contrast.ts`, which also retires three duplicated helpers.

Measuring cannot rescue every accent: for some backgrounds neither black nor white reaches 4.5:1. That is a property of the accent, not something a text colour can fix, so the appearance editor now warns on that pair instead of silently shipping an unreadable button.

## archived-48 — a nav item leading to an empty form

The Site Configuration menu seeded a **Features** item pointing at `configFeatures`, but `SettingGroup.Features` has no settings defined, so it led to a form reading "No settings in this group yet."

Removed the seeded row, plus an idempotent `DELETE` for installs that already have it, following the existing `PluginsIconMenuRemovalSql` pattern.

**Kept** the template, route and thumbnail: the group is a declared extension point that the registry's own "adding a new feature flag" instructions name, so the page should exist for whoever adds the first flag. What is wrong today is only that the navigation promises something that is not there. Moving General's single setting into Features would have relocated the empty page, not removed it.

## Verification

Full `AutoNate.Web.Tests` **1465 / 0** (4 new), full E2E **154 / 0** (1 new). `tsc -b`, `vite build` and lint clean.

Every new guard red-checked: the watermark test by gutting the `DELETE`, the contrast test by restoring the YIQ heuristic, the Features test by restoring the seed row. archived-43 is a deletion, so its check is the inverse — 1465 tests exercising grant/role/group mutations pass with the bump gone, which is what "no consumer" has to mean.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Y5ie3qTEptr4MjYw5i6a5F

<details><summary>Comment — nathanpond, 2026-09-01</summary>

Content landed on `master` via archived-189.

This PR was auto-closed by GitHub when its base branch was deleted during the stack merge, not abandoned. Every commit from this batch is an ancestor of `fix/bugs-batch13-authz`, which archived-189 merged — verified commit-by-commit before merging. The write-up above still describes what shipped.

</details>

---

## archived-181 — fix: make Rebuild work, gate accessibility permanently, unpin brittle locators (archived-112, archived-40, archived-92)

`MERGED (merged 2026-09-01)` · nathanpond · opened 2026-09-01 · `fix/bugs-batch10` → `fix/bugs-batch9-backend`

Closes archived-112
Closes archived-40
Closes archived-92

> Stacked on archived-180 → archived-179. Review those first; the diff below is the last four commits.

## archived-112 — Rebuild was decorative

`POST /api/admin/projections/{name}/rebuild` returned **400 for every projection**. `BackfillRunner` resolves `IProjectionBackfillSource<TSource>` and throws when none is registered, and nothing implemented the interface — so the recovery path in `docs/projection-framework/operations.md` did not work.

Two comments already promised these existed: `FlowableExecutionPollingFeed` points at *"FlowableExecutionBackfillSource (defined separately)"*, and `RecordActivityRollupFeed` says *"the full historical recompute lives on BackfillRunner via the matching IProjectionBackfillSource"*. Neither did.

Five sources, one per registered projection. Each re-emits from the same call its polling feed uses, minus the per-tick bounds that keep a tick cheap: variables cover every cached instance rather than a sample of active ones, history starts from the beginning rather than the watermark (re-reading what the watermark says was seen is the *point* of a rebuild), and the rollup drops its day-window predicate.

Where Flowable's API bounds what can be enumerated, the class says so rather than implying more: an execution rebuild restores the cache to what steady-state polling would have produced; it cannot resurrect instances aged out of Flowable itself. An operator can trust a backfill that states its reach.

One framework fix came with it: `BackfillRunner` resolved the source from the **root** provider, so a scoped source failed with *"Cannot resolve scoped service … from root provider"*. It now resolves inside a scope that lives for the whole enumeration — the right fix, since a backfill source legitimately depends on scoped services, and the alternative was to distort every source's lifetime to suit the runner.

Tests: one drives the real projection list and rebuilds each, so adding a projection without a backfill source fails there rather than in production; the other proves a backfill actually repairs data, recomputing a bucket **400 days old** that the feed's recent-window recompute can never reach.

## archived-40 — the permanent version of every 508 fix

**Lint ratchet.** The nine jsx-a11y rules were warnings inside a total budget, so a new violation was free until the budget ran out. They are now **errors** for the sixteen directories that are already clean — a list derived by running eslint, not by aspiration, so `npm run lint` passes the moment it lands. Fixing a directory and moving it into the list is the ratchet. Verified by planting a `<div onClick>` in `src/pages/records`: 2 errors, exit 1.

**Axe scan.** Four signed-in pages, failing on critical/serious impacts under `wcag2a`/`wcag2aa`. Deliberately not moderate/minor — those include advisory best-practice findings that would make this a noise generator, and a gate nobody trusts gets skipped. eslint reads source patterns, axe reads the rendered DOM; neither substitutes for the other.

It found four real defects on arrival, all fixed here — the most important one being that **the shipped theme still failed WCAG**:

- `sidebar_section_color` `#adb5bd` is **2.07:1** on the white sidebar (needs 4.5:1) and `primary_accent_color` `#00acac` is **2.80:1** on the surface (needs 3.0:1). Both had been corrected in the SPA's `DEFAULT_SITE_APPEARANCE` and **never mirrored into the server-side seed** — and the seed is what a real install reads, so every install shipped the failing values while the constant said otherwise. Fixed in the seed, with a guarded `UPDATE` that only touches rows still holding the exact old defaults so a deliberate admin choice survives. The new gate asserts the editor raises no advisory for shipped defaults, aimed at the database rather than the constant — a check against the constant would have passed happily through this entire bug.
- `ChatPaletteModal` passed `aria-label` to `<Modal>`, which lands it on the root `<div>`. `aria-label` is prohibited on a generic element, so axe flagged it and screen readers ignored it — the palette had **no name at all**. Now a `VisuallyHidden` `Modal.Title`, which is what Mantine wires `aria-labelledby` to.
- 19 eyedropper buttons on the appearance page announced as just "button".
- Home's stat cards put white text at 0.85 opacity on light fills; every card measured below 4.5:1. Opacity removed, fills darkened. Orange needed a raw hex — even Mantine's `orange.9` is 4.30:1, just under.

## archived-92 — a position is not a handle

The pipelines specs reached rows through `main.Locator("table").First` → `Locator("tbody tr").First`, with a comment explaining the semantic option had been judged fragile. That couples the assertion to structure and ordering: change the sort or add a table above, and the spec silently targets a different row instead of failing.

`DataTable` gains `getRowTestId`; the specs now ask for "a run row" and "a step row". Two things came out of doing it:

- The wrapper's claim that *"mantine-datatable doesn't expose per-row aria props directly"* is wrong — `customRowAttributes` does exactly that. The dead `{getRowAriaLabel ? null : null}` discard is gone and the callback is honoured, so these rows also get accessible names. **Note:** this touches the same block as archived-178; merge that first and the conflict is trivial.
- My first prefix, `pipeline-run-`, is also a prefix of `pipeline-run-step-`, so the run selector matched step rows. It passed only because the runs table happens to render first — exactly the incidental ordering dependency this change exists to remove.

**Not changed:** the `[contenteditable='true']` / `.cm-editor` / `.excalidraw` locators the issue also lists. Those address third-party editing surfaces we do not render, and they name the editable region rather than depending on structure or ordering. Swapping them for a wrapper testid would add indirection without removing coupling.

## Verification

Full `AutoNate.Web.Tests` **1467 / 0**, full E2E **159 / 0**. `tsc -b`, `vite build` and lint clean.

Every new guard red-checked. One self-inflicted failure worth recording: naming the eyedropper buttons made `GetByLabel("Primary accent")` match two elements, since `GetByLabel` matches substrings — caught by the full run, not by the isolated one, and fixed by asking for the textbox by exact name.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Y5ie3qTEptr4MjYw5i6a5F

---

## archived-187 — test: cover the untested endpoint surfaces, add CI, and fix what that uncovered (archived-79, archived-80-archived-84, archived-87, archived-90, archived-91)

`CLOSED` · nathanpond · opened 2026-09-01 · `fix/bugs-batch11-coverage` → `fix/bugs-batch10`

Closes archived-79
Closes archived-80
Closes archived-81
Closes archived-82
Closes archived-83
Closes archived-84
Closes archived-87
Closes archived-90
Closes archived-91

> Stacked on archived-181 → archived-180 → archived-179 → archived-178. Review those first.

The test-coverage cluster, plus CI to enforce it — and the defects that writing the tests uncovered. Backend suite goes from **1467 to 1645** tests.

## The bug the coverage found

DataStore, DataConnector, Dataset, Query and Pipeline had selector compilers registered — so list endpoints filtered correctly — but **no `IInstanceAuthorizer`**. `Authorizer.ComputeDecisionAsync` denies with *"no instance handler for kind '<kind>'"* when none is registered, so every `RequirePermission` endpoint for those five kinds answered **403 to every non-super-admin, including the owner of the row being requested**. Datastore file upload/download/copy/table-preview, connector runs, dataset detail, saved-query detail and pipeline detail were all unreachable.

Measured, not inferred: with view/edit/create grants on `/datastore/*` — and again with an exact `/datastore/{id}` — `GET /api/datastores/{id}` returned 403; after registering the handler, 200. The two registration blocks sit adjacent in `Program.cs`, so this reads as compilers added without their handlers.

## archived-84 — an assertion that could not fail, hiding a broken button

The spec clicked the UI delete, then issued an unconditional API `DELETE` for the same conversation *before* asserting it was gone. Removing the mask showed the affordance never worked: the conversation was still readable 10 seconds later. The confirmation is a Mantine modal and the spec registered a `page.Dialog` handler — which only fires for `window.confirm` — so nothing was ever confirmed. It now clicks the modal and asserts the server 404s.

## archived-87 — proving the gates deny

`AuthorizationGatePresenceTests` reads route metadata; it never calls the endpoint, so it cannot tell a correct gate from one wired to the wrong `(EntityKind, Action)` pair. Table-driven over the eight kind-level routes, three cases each: denied without a grant, allowed with the declared one, and — the case presence-checking cannot see — **still denied when holding a different kind's grant for the same action**. Red-checked by re-gating `/api/pipelines` on `Dataset:List`: 2 of 24 fail, and they are the two that name the mis-wiring.

## archived-79 — CI

Three jobs so a red build says *what* broke. Two details that are not obvious from the file: NATS is started with `docker run` because service containers cannot pass a `command` and NATS only enables JetStream with `--jetstream`; and the E2E job installs Node despite running no npm step, because its fixture launches the app with `-p:BuildSpa=true` and deletes `wwwroot` first — my first draft passed the SPA bundle through as an artifact, which would have been thrown away.

Deliberately **not** `-warnaserror`, which the issue suggested: the tree carries analyzer warnings in code this workflow does not own, so it would be red on arrival, which is how a gate gets switched off.

## archived-80–archived-83, archived-90, archived-91 — the coverage itself

Yjs tickets and both shared-secret filters (replay, tampering, absent/empty/wrong secrets, and that a wrong secret answers identically for real and non-existent documents); datastore files and the dataset file-source parser dispatch; notes/pages writes and version restore — restore has to restore the *content*, not just return 204; document bindings and comments with the full View/Comment/Edit/RefreshBindings gate matrix; and privilege mutation, where every test reads the store back so a 403 that still wrote a row fails.

## Five findings filed, not fixed here

Each backed by a passing test that asserts the current behaviour, so they invert when fixed: **archived-182** (revoke gated kind-level — a one-role assign grant strips any role from anyone), **archived-183** (`preview-file-source` reads any datastore file with no DataStore authorization), **archived-184** (that endpoint 500s and leaks a stack trace on a `.keep` placeholder), **archived-185** (Yjs ticket existence oracle), **archived-186** (four smaller findings).

## One fixed: `role:assign` was a path to super-admin

Nothing restricted which role could be handed out or to whom, and the authorizer re-reads assignments per request, so the holder could assign themselves SuperAdmin and be one on the next call. Two guards: only a super-admin may hand out SuperAdmin, and nobody may self-assign unless they already are one. Assigning to *other* people is untouched.

Deliberate stopping point: two colluding assigners can still escalate each other. The general rule — you may only delegate permissions you already hold — needs a role-subset comparison this codebase does not have.

The full suite then caught a flaw in that fix: the guard denied self-assignment even with authorization **switched off**, breaking two pre-existing tests. They were right to fail — every other decision point short-circuits to allow when authorization is disabled or in read-only rollout, and a guard that denies where the rest of the system allows would break the staged rollout those options exist for. It now runs only under full enforcement.

## Verification

Full `AutoNate.Web.Tests` **1645 / 0**, full E2E **159 / 0**.

Three test-side defects were mine: Postgres reserializes `jsonb` — whitespace normalized, object keys re-emitted in its own order — so comparing raw strings tested Postgres's formatter rather than the endpoint. Both suites canonicalize before comparing now.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Y5ie3qTEptr4MjYw5i6a5F

<details><summary>Comment — nathanpond, 2026-09-01</summary>

Content landed on `master` via archived-189.

This PR was auto-closed by GitHub when its base branch was deleted during the stack merge, not abandoned. Every commit from this batch is an ancestor of `fix/bugs-batch13-authz`, which archived-189 merged — verified commit-by-commit before merging. The write-up above still describes what shipped.

</details>

---

## archived-188 — fix(spa): gate the admin shell and record delete on permission; unblock the typed-field journey (archived-85, archived-86)

`CLOSED` · nathanpond · opened 2026-09-01 · `fix/bugs-batch12-gating` → `fix/bugs-batch11-coverage`

Closes archived-85

Refs archived-86 — partially addressed; see the scope note at the end.

> Stacked on archived-187 → archived-181 → archived-180 → archived-179 → archived-178. Review those first.

## archived-85 — two journeys blocked on the affordances they were meant to test

Admin routes sat under the authenticated `AppShell` with no client-side guard, so a user with no grants could deep-link to `/admin/config` and get the full chrome — nav, headings, empty tables — while every API call behind it returned 403. `RecordDetail` rendered its delete action unconditionally, so a user without `record:delete` was offered a button whose every click ended in a 403.

The backend held in both cases, so neither was exposure. They were **affordance** defects, and those carry their own cost: the page looks broken rather than forbidden, and the user cannot tell which — nor what to ask an administrator for.

`PermissionRoute` checks the same `(kind, action)` the server declares, so the guard cannot drift from what is actually enforced. It renders an explicit *"this section needs siteconfig view — ask an administrator"* panel rather than redirecting: a silent bounce to the dashboard is indistinguishable from a dead link and leaves someone who genuinely needs access with nothing to act on. The record delete reuses the instance-level check that already existed for exactly this (`usePermissionChecks`), mirroring how `WorkflowExecutions` gates Delete All.

Three specs, each with a positive twin so an absent element is proven to be the permission check rather than a mis-locator — the record one grants delete mid-test and re-asserts. Both fixes red-checked by reverting the SPA changes.

## archived-86 — the starting point it names, and three corrected rows

E2E-061's blocker was *"the current record seeder creates schema-less record types"*. `ApiSeeder.AddRecordTypeFieldAsync` builds a typed schema, and `RecordsAdvancedTests.cs` — one of the three named-but-missing spec files — covers text/number/option fields plus an option filter narrowing the list.

Making it targetable meant fixing a real gap: the three controls in a filter row had **no accessible names at all**, so a screen reader announced "combo box" three times over and a spec had nothing to ask for but position. Named now, which is why the spec reads as field/operator/value rather than nth-select.

Verified by inverting rather than trusting a green: selecting `silver` instead of `gold` swaps which record survives, so the filter is genuinely applied and the assertion is not passing on a coincidentally short list.

Three rows corrected. **E2E-029** and **E2E-066** were still marked BLOCKED but had already been unblocked earlier in this stack — by the DOCX import finalize fix (archived-173), and by establishing that the appearance "revert" was a racy spec rather than a product defect (archived-172). E2E-061 is marked PARTIAL: the filter matrix is covered, column-picker selection is not.

**BLOCKED count 17 → 14.**

### Scope note — why the other 14 are not in this PR

They are not unwritten tests; they are missing fixture capability:

- an injectable agent stream (E2E-054, E2E-056)
- deterministic mutation for Yjs-backed editors (E2E-052, E2E-053)
- stable BPMN canvas node selection (E2E-036, E2E-037, E2E-059)
- operator-state seeding for Flowable (E2E-060)

Each is its own piece of work. Writing specs against them before the hooks exist would produce exactly the flaky, position-based tests archived-92 was filed about — so archived-86 stays open with an accurate count rather than being closed over 14 skipped specs.

## Verification

Full E2E **163 / 0** (four new specs). Full `AutoNate.Web.Tests` **1645 / 0**.

The combined run showed one failure, `NotesQueryEndpointTests.FromNotes_OnEmpty_ReturnsSchemaColumns` — the artifact recorded on archived-163. It ran E2E first, which empties `AutoNate.Web/wwwroot` and deletes the static-web-asset manifests; the build that followed was a no-op because no sources had changed, so the manifests were never regenerated. After `--no-incremental`, 8/8 in isolation.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Y5ie3qTEptr4MjYw5i6a5F

<details><summary>Comment — nathanpond, 2026-09-01</summary>

Content landed on `master` via archived-189.

This PR was auto-closed by GitHub when its base branch was deleted during the stack merge, not abandoned. Every commit from this batch is an ancestor of `fix/bugs-batch13-authz`, which archived-189 merged — verified commit-by-commit before merging. The write-up above still describes what shipped.

</details>

---

## archived-189 — fix: land bug batches 9-13 (authorization, projections, a11y gates, CI, endpoint coverage)

`MERGED (merged 2026-09-01)` · nathanpond · opened 2026-09-01 · `fix/bugs-batch13-authz` → `master`

Closes archived-182
Closes archived-183
Closes archived-43
Closes archived-44
Closes archived-47
Closes archived-14
Closes archived-48
Closes archived-112
Closes archived-40
Closes archived-92
Closes archived-79
Closes archived-80
Closes archived-81
Closes archived-82
Closes archived-83
Closes archived-84
Closes archived-87
Closes archived-90
Closes archived-91
Closes archived-85

**Retargeted to `master` and now carries batches 9 through 13.** archived-180 and archived-187 were auto-closed by GitHub when their base branches were deleted during the merge of archived-179 and archived-181 — their content was never lost, and every commit from those batches is an ancestor of this branch (verified commit-by-commit, not by branch name). Merging this lands all of it.

The individual batch write-ups are on archived-180, archived-187, archived-188 and the original archived-189 body; this is the summary.

## Authorization

- **archived-182** — revoke was gated `RequireKindPermission(Role, Assign)`, which never resolved the assignment, so a grant naming one throwaway role could strip **anybody's membership of any role, SuperAdmin included**. Now resolves the assignment and authorizes against the role it names.
- **archived-183** — `preview-file-source` checked only `Dataset:Create` while reading an arbitrary file from an arbitrary store named in the body. Now authorizes `(DataStore, View)` against the store it reads.
- **archived-87** — the kind-level gates are proved to *deny*: three cases per route, including "still denied when holding a different kind's grant", which is the case route-metadata inspection structurally cannot see.
- **archived-91**, **archived-90** — privilege-mutation and document binding/comment coverage; every test reads state back, so a 403 that still wrote a row fails.
- Registered the **five missing `IInstanceAuthorizer`s**. DataStore, DataConnector, Dataset, Query and Pipeline had selector compilers but no handler, so every instance-level endpoint for them returned 403 to everyone but super-admins — including the row's owner.
- `role:assign` no longer reaches SuperAdmin (self-assignment and SuperAdmin hand-out both refused, under full enforcement only).

## Correctness

- **#112** — `Rebuild` returned 400 for every projection because no `IProjectionBackfillSource` existed. Five implemented; `BackfillRunner` now resolves them in a scope.
- **archived-43**, **archived-44**, **archived-47**, **archived-48** — retired a write nothing read, gave the dead-letter table a reader and a replay, wired `reset-watermark` to a button, stopped seeding a nav item that led to an empty form.
- **archived-14** — button text colour picked by measured WCAG contrast instead of YIQ brightness.
- `DatastoresDatabaseInitializer`'s probe-then-`CREATE DATABASE` TOCTOU no longer fails app startup when two instances start at once.

## Accessibility

- **archived-40** — jsx-a11y is now an **error** for sixteen already-clean directories, plus an axe scan over four signed-in pages. It found four real defects on arrival, including that **the shipped theme still failed WCAG**: the accessible defaults had been fixed in the SPA constant and never mirrored into the server-side seed a real install reads.
- **archived-85** — the admin shell and the record delete action are gated on permission, so a user without access sees "you don't have access" rather than a shell whose every call 403s.

## Tests and CI

- **archived-79** — CI runs on every push and PR. Backend and SPA gates run in full; E2E runs 155 of 163, with the Flowable- and Dapr-dependent specs excluded **by trait** and the gap recorded in `.n8/decisions.md`.
- **archived-84** — the conversation-delete spec could not fail. Removing the mask showed the button never worked in test: the spec waited on `window.confirm` for what is a Mantine modal.
- **archived-80–archived-83**, **archived-92** — Yjs ticket/secret-filter coverage, datastore and parser-dispatch coverage, notes write and version-restore coverage, and pipeline rows addressed by test-id rather than position.

## Verification

Full `AutoNate.Web.Tests` **1647 / 0**, full E2E **164 / 0**, on the merged tree.

One backend failure appeared in the full run — `CreateAsync_KeysAreSequentialUnderConcurrency` — and passes 3/3 in isolation. Called a flake under load rather than dressed up.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Y5ie3qTEptr4MjYw5i6a5F

---

## archived-190 — chore(deps-dev): bump browserslist from 4.28.2 to 4.28.8 in /src/AutoNate.Spa

`MERGED (merged 2026-09-01)` · app/dependabot · opened 2026-09-01 · `dependabot/npm_and_yarn/src/AutoNate.Spa/browserslist-4.28.8` → `master`

Bumps [browserslist](https://github.com/browserslist/browserslist) from 4.28.2 to 4.28.8.
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/browserslist/browserslist/releases">browserslist's releases</a>.</em></p>
<blockquote>
<h2>4.28.8</h2>
<ul>
<li>Fixed <code>including kaios</code> in baseline queries (by <a href="https://github.com/Jaybhade"><code>@​Jaybhade</code></a>).</li>
</ul>
<h2>4.28.7</h2>
<ul>
<li>Improved parsing performance.</li>
<li>Fixed unbounded memory growth (by <a href="https://github.com/alanturing881"><code>@​alanturing881</code></a>).</li>
<li>Fixed prototype write issue (by <a href="https://github.com/alanturing881"><code>@​alanturing881</code></a>).</li>
</ul>
<h2>4.28.6</h2>
<ul>
<li>Fixed Electron version queries (by <a href="https://github.com/spokodev"><code>@​spokodev</code></a>).</li>
</ul>
<h2>4.28.5</h2>
<ul>
<li>Fixed <code>&gt;</code> and <code>&gt;=</code> queries (by <a href="https://github.com/spokodev"><code>@​spokodev</code></a>).</li>
</ul>
<h2>4.28.4</h2>
<ul>
<li>Fixed <code>SyntaxError</code> regression of 4.28.3.</li>
</ul>
<h2>4.28.3</h2>
<ul>
<li>Fixed baseline query case-insensitivity (by <a href="https://github.com/swwind"><code>@​swwind</code></a>).</li>
</ul>
</blockquote>
</details>
<details>
<summary>Changelog</summary>
<p><em>Sourced from <a href="https://github.com/browserslist/browserslist/blob/main/CHANGELOG.md">browserslist's changelog</a>.</em></p>
<blockquote>
<h2>4.28.8</h2>
<ul>
<li>Fixed <code>including kaios</code> in baseline queries (by <a href="https://github.com/Jaybhade"><code>@​Jaybhade</code></a>).</li>
</ul>
<h2>4.28.7</h2>
<ul>
<li>Improved parsing performance.</li>
<li>Fixed unbounded memory growth (by <a href="https://github.com/alanturing881"><code>@​alanturing881</code></a>).</li>
<li>Fixed prototype write issue (by <a href="https://github.com/alanturing881"><code>@​alanturing881</code></a>).</li>
</ul>
<h2>4.28.6</h2>
<ul>
<li>Fixed Electron version queries (by <a href="https://github.com/spokodev"><code>@​spokodev</code></a>).</li>
</ul>
<h2>4.28.5</h2>
<ul>
<li>Fixed <code>&gt;</code> and <code>&gt;=</code> queries (by <a href="https://github.com/spokodev"><code>@​spokodev</code></a>).</li>
</ul>
<h2>4.28.4</h2>
<ul>
<li>Fixed <code>SyntaxError</code> regression of 4.28.3.</li>
</ul>
<h2>4.28.3</h2>
<ul>
<li>Fixed baseline query case-insensitivity (by <a href="https://github.com/swwind"><code>@​swwind</code></a>).</li>
</ul>
</blockquote>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/browserslist/browserslist/commit/f2f2e6cfb01bb4942941d328737546f4e2ae41ad"><code>f2f2e6c</code></a> Release 4.28.8 version</li>
<li><a href="https://github.com/browserslist/browserslist/commit/d0787c88fa29ba895fea51cfe921232c7b5d1377"><code>d0787c8</code></a> Update dependencies</li>
<li><a href="https://github.com/browserslist/browserslist/commit/fcf8fa9857b30ccdf801a548f5d09d3c4ff0d43f"><code>fcf8fa9</code></a> Merge pull request <a href="https://redirect.github.com/browserslist/browserslist/issues/939">#939</a> from Jaybhade/fix/baseline-kaios-without-downstream</li>
<li><a href="https://github.com/browserslist/browserslist/commit/57ecd64454e9252afdd6a7e76926e13dda48a38c"><code>57ecd64</code></a> fix: support &quot;including kaios&quot; without downstream</li>
<li><a href="https://github.com/browserslist/browserslist/commit/093a0f67bb0becda55235d767b134df3197c54a1"><code>093a0f6</code></a> Update EM banner</li>
<li><a href="https://github.com/browserslist/browserslist/commit/b637868045806d2fba4c24eb0060e4cc8b1db276"><code>b637868</code></a> Release 4.28.7 version</li>
<li><a href="https://github.com/browserslist/browserslist/commit/313f4659b9f985ade89d1d6a54a860371c41cc46"><code>313f465</code></a> Update dependencies</li>
<li><a href="https://github.com/browserslist/browserslist/commit/c935c5a206f8b13db8846818bc03643e147dcbdf"><code>c935c5a</code></a> Fix regexp performance</li>
<li><a href="https://github.com/browserslist/browserslist/commit/d7e9e653cb53399065943f59f0b3063987b0a008"><code>d7e9e65</code></a> Rewrite structure parsing to make it always fast</li>
<li><a href="https://github.com/browserslist/browserslist/commit/ec4a55efd76bdfa506ec7ce4fea1691559e9ca8f"><code>ec4a55e</code></a> Fix import order</li>
<li>Additional commits viewable in <a href="https://github.com/browserslist/browserslist/compare/4.28.2...4.28.8">compare view</a></li>
</ul>
</details>
<details>
<summary>Maintainer changes</summary>
<p>This version was pushed to npm by <a href="https://www.npmjs.com/~GitHub%20Actions">GitHub Actions</a>, a new releaser for browserslist since your current version.</p>
</details>
<br />


[![Dependabot compatibility score](https://dependabot-badges.githubapp.com/badges/compatibility_score?dependency-name=browserslist&package-manager=npm_and_yarn&previous-version=4.28.2&new-version=4.28.8)](https://docs.github.com/en/github/managing-security-vulnerabilities/about-dependabot-security-updates#about-compatibility-scores)

Dependabot will resolve any conflicts with this PR as long as you don't alter it yourself. You can also trigger a rebase manually by commenting `@dependabot rebase`.

[//]: # (dependabot-automerge-start)
[//]: # (dependabot-automerge-end)

---

<details>
<summary>Dependabot commands and options</summary>
<br />

You can trigger Dependabot actions by commenting on this PR:
- `@dependabot rebase` will rebase this PR
- `@dependabot recreate` will recreate this PR, overwriting any edits that have been made to it
- `@dependabot show <dependency name> ignore conditions` will show all of the ignore conditions of the specified dependency
- `@dependabot ignore this major version` will close this PR and stop Dependabot creating any more for this major version (unless you reopen the PR or upgrade to it yourself)
- `@dependabot ignore this minor version` will close this PR and stop Dependabot creating any more for this minor version (unless you reopen the PR or upgrade to it yourself)
- `@dependabot ignore this dependency` will close this PR and stop Dependabot creating any more for this dependency (unless you reopen the PR or upgrade to it yourself)
You can disable automated security fix PRs for this repo from the [Security Alerts page](https://github.com/nathanpond/AutoNate/network/alerts).

</details>

---

## archived-191 — fix: close the preview 500, the ticket existence oracle, and three of archived-186

`MERGED (merged 2026-09-01)` · nathanpond · opened 2026-09-01 · `fix/bugs-batch14-hardening` → `master`

Closes archived-184
Closes archived-185
Closes archived-186

The three findings from the endpoint-coverage pass, plus a suite-wide test defect that showed up while verifying them.

## archived-184 — a 500 that handed back a stack trace

A folder's `.keep` placeholder row carries an empty `storage_key`, and the endpoint downloaded the file before dispatching to a parser. `ResolveAbsolutePath("")` resolves to the datastores **root directory**, so `File.OpenRead` threw — and with no exception-handling middleware the caller got a 500 carrying the exception text and stack. Reachable by anyone holding `dataset:create`.

The folder branch already filtered `.keep`; the file branch does now too, and answers 404, because a placeholder is not a file anyone can preview.

## archived-185 — the ticket endpoint confirmed what existed

The note and `documents:` branches answered 404 for a missing row and 403 for one the caller could not see, so any signed-in user with no grants could probe note and document ids for existence, one GUID at a time.

Existence is still resolved first but only **disclosed** after the authorization check: an unauthorized caller gets 403 either way, while someone who could have seen the row still gets the informative 404. The note-kind-mismatch 400 is deferred the same way — it also confirmed the note existed, which the issue didn't mention. The `page:` branch always had this shape; the other two now match it.

## archived-186 — three of four

1. **Overrides accept user and group only.** The grant store's allowlist also contains `role` — right for an admin using `/api/admin/grants` — but nothing narrowed it here, so anyone with Edit on a folder could attach a resource grant to a role, **SuperAdmin included**, through an endpoint whose own header describes user/group sharing. The store's rejection message also claimed user/group while enforcing three kinds; it now names what it actually allows.
3. **Comment create and reply answer 409, not 500.** The pre-check is a TOCTOU the code already described as *"a real-world race we accept"*, but nothing caught the `DbUpdateException` — so the loser got an unhandled 500 rather than the 409 the same handler promises three lines earlier, and the client's retry keys off that 409.
4. **`refresh-all` returns one shape.** The zero-binding branch returned `DocumentBindingListResponse` while every other path returned `RefreshAllResponse`, so a client reading `failures.length` got `undefined` for a document with no bindings — a crash on the one input guaranteed to be uninteresting.

**Item 2 is deliberately unchanged.** Neither privilege store checks that the principal exists. Pre-provisioning access for an id that appears on first sign-in is how this works with an external IdP — this codebase has `auth_source` / `idp_key` — so an existence check would break granting ahead of someone's first login. That is a product decision rather than a defect, and the tests pinning it now say so rather than implying an oversight. Happy to add the validation if you'd rather have it.

## The suite defect found while verifying

`CreateAsync_KeysAreSequentialUnderConcurrency` failed two full runs in a row with `53300: sorry, too many clients already`. I called it a flake the first time; it was not.

Every test class builds its own database, so it gets its own connection string and therefore its own Npgsql pool — default maximum **100 per pool**. With xunit running classes in parallel that multiplies past Postgres's own `max_connections`. The concurrency test opens twenty connections at once so it usually drew the short straw, but nothing about it was wrong: the cause was suite-wide, and it would have kept biting CI, whose Postgres service has the same default.

Pools are capped at 10 per test database; the administrative create/drop connections are no longer pooled at all.

## Verification

Full `AutoNate.Web.Tests` **1648 / 0** in 12m04 — against 12m56 for the run that failed, so bounding the pools cost nothing. Full E2E **164 / 0**. All four fixes red-checked.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Y5ie3qTEptr4MjYw5i6a5F

---

## archived-192 — fix: survive the CREATE ROLE catalog race on concurrent startup

`MERGED (merged 2026-09-01)` · nathanpond · opened 2026-09-01 · `fix/role-create-race` → `master`

Master CI failed on `HealthEndpointEnforcementTests` with `23505: duplicate key value violates unique constraint pg_authid_rolname_index` — a test that has nothing to do with roles. It was simply booting a host at the wrong moment.

`EnsureWriterRoleAsync` already anticipated the race: advisory lock keyed by role name, plus `EXCEPTION WHEN duplicate_object`. Two things were wrong.

**The advisory lock does not serialize what its comment claimed.** An advisory lock tag includes the database oid, so two connections to *different* databases take different locks — and every host connects to its own datastores database, which is exactly the case the lock was meant to cover. The comment now says what actually holds.

**The EXCEPTION clause caught only `duplicate_object` (42710).** When two `CREATE ROLE`s interleave inside the catalog insert the loser sees `23505` on `pg_authid_rolname_index`, which fell straight through. Both forms are handled now — and the EXCEPTION clause, not the lock, is what makes this safe.

Same shape as the `CREATE DATABASE` race fixed earlier in this stack, and real beyond the test suite for the same reason: two instances starting at once — a rolling deploy, a scaled-out replica set — where today one fails startup.

## Where master stands

That run: SPA pass, **E2E pass** (the folder strict-mode fix worked), backend ran to completion in 34m under the raised 75m ceiling — so the timeout and pool fixes did their job. This is the last thing between master and green.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Y5ie3qTEptr4MjYw5i6a5F

---

## archived-194 — 0.1 release readiness and the Auton8 rename

`MERGED (merged 2026-09-02)` · nathanpond · opened 2026-09-01 · `feat/0.1-public-release` → `master`

Closes archived-193

Everything that has to be true before this repository can be made public, plus the AutoNate → Auton8 rename. The history rewrite and the flip itself are **not** in this PR — see the end.

## The credential (archived-193)

`infra/postgres/init/02-create-autonate-app-schema.sql` seeded `admin` with its `password_hash` *and* `password_salt` in the file. Both halves of the PBKDF2 verification were present, so this was not a hash to crack — the plaintext is `admin`. The INSERT was ungated by environment, and `AssignSuperAdminToAllExistingUsers` defaulted true, whose backfill grants SuperAdmin to every row in `local_users`. Every install that ran the script came up with a super-admin whose password is public. Publishing the repo would have published the credential, but the finding never depended on that.

Removing the seed alone would have made a clean database unusable: no registration page, no setup wizard, and `POST /api/users` requires an authenticated caller. That is presumably why it shipped ungated — it was load-bearing. So the removal and the replacement are one change: `BootstrapAdminOptions` + `DatabaseSchemaInitializer.EnsureBootstrapAdminAsync` create one administrator when `local_users` is empty **and** both a username and password are configured. Configure nothing and you get nothing — a logged message naming the two settings, never a default account.

The bootstrap account grants itself SuperAdmin, which is what lets `AssignSuperAdminToAllExistingUsers` stop being load-bearing and ship **false** in both appsettings files. Enabling it promotes the entire existing user table at once; it is a migration aid for deployments predating role assignments, not first-run setup.

Existing installs are deliberately untouched (the bootstrap skips a non-empty `local_users`) — **their `admin` password is public and must be changed.**

### What the tests found

Two regressions, both caught by the full suite rather than reasoning:

**The pinned GUID is the enforcement suites' *limited* principal.** Coupling the SuperAdmin grant to account creation made `11111111-…-111111111111` a super-admin, and roughly twenty enforcement suites use exactly that user as the one they grant a single narrow permission to. They went from asserting 403 to passing vacuously. `Bootstrap:GrantSuperAdmin` is separable for this reason, and the factory pins it false; `BootstrapAdminTests` has both twins so the two can never silently agree.

**Suites that never boot a host.** `PostgresTestDatabase` replays the init SQL directly, so the seed was their only source of the `admin` row. That seeding moved into `PostgresTestDatabase` — test code, where a test credential belongs — and it hashes at runtime rather than storing a hash. `AutoNateWebApplicationFactory` opts out so the app's own bootstrap is what runs under test.

Red-checked: reintroducing a default credential fails 5 of the 10 bootstrap tests; removing the grant fails the grant test.

## 0.1 content

- **The last stub.** `configFormMappings` rendered "This section is a stub. Functionality coming soon." — the same defect class as archived-42, which fixed five siblings and missed this one. Component deleted; the seeded menu row and page template now seed disabled, with a one-shot `retire_form_mappings_stub_v1` migration for installs that already have them. The `appRoutes` entry had to go too: `template()` indexes `PAGE_TEMPLATES`, and a miss is `undefined` rather than a type error, so leaving it would have mounted a blank page instead of falling through to NotFound.
- **The landing page.** Four StatCards presented as a metrics strip and carried no metrics — one read "THEME STATUS / Mantine", and the other three were the same three destinations, with the same icons, as the quick links directly beneath them. Grid removed rather than wired to invented counts; links broadened past workflow-only; retitled, since the product is no longer just automation.
- **Hygiene.** Apache-2.0 `LICENSE`, `SECURITY.md` (private reporting, honest response expectations rather than an invented SLA), `CONTRIBUTING.md`, and credential-shaped `.gitignore` patterns. The plan's "untrack `AutoNate.sln.DotSettings.user`" item turned out to be already done.
- **README.** Was a runbook titled "AutoNate Local Development". Now a landing page; the runbook is `docs/DEVELOPMENT.md` and `docs/DEPLOYMENT.md`, both of which gained the first-administrator step that had no home before.
- **Loopback.** Postgres, Flowable, Redis, Hocuspocus, both Dapr services and the dashboard were published on `0.0.0.0`. NATS was already pinned with a comment claiming it matched "the Dapr endpoints in this file"; it does now.
- **0.1.0** across `Directory.Build.props` and the SPA.

## Two bugs found on the way

**The login cover 404s.** Three backend appearance defaults pointed at `/spa/assets/img/login-bg/login-bg-17.jpg`. Nothing serves a `/spa` request path — static files are served at the root — and the SPA-side default disagreed, pointing at `space.jpg`. All four now agree, with a guarded migration for installs still carrying the broken URL. That also removed the last reference to an image carrying a paid theme's demo filename, which is now deleted along with a stray extensionless duplicate of `space.jpg`.

**Five statements in `docs/codebase/` assert the repo has no CI.** True when generated, false since archived-79. Corrected, and every page now carries a provenance banner — they are a snapshot of shape, not a current defect list. Also `AUTONATE_DATA_ROOT`, documented in the README and absent from the code; the real key is `Data__Root`.

## The rename

Public surfaces only. The four `SiteAppearance` seed/default copies move in lockstep — they had already drifted — plus a **guarded** `UPDATE`, per column, on the old value: without it every existing install keeps the old name while a fresh one shows the new, and an administrator's own branding is never overwritten.

Then the assistant's own identity, the tab title, ~24 user-visible strings across 11 components, and the docs.

Internal identifiers stay `AutoNate`, and this is a decision rather than an oversight — renaming the DataProtection purposes makes every stored provider secret undecryptable, the `.docx` markers orphan every bound document, and the plugin ABI breaks third-party plugins. `CLAUDE.md` and `CONTRIBUTING.md` both now say so, so it does not read as sloppiness.

Two plan estimates were wrong in the same direction and are worth recording: of 34 `AutoNate` hits in `EventCatalog.cs`, 33 name the `AutoNate.Web` *assembly* and correctly stay — one is product prose. The E2E specs locate the agent sidebar by its `aria-label`, so renaming it is a test change too.

## A third regression: the version bump broke the plugin ABI

`<Version>0.1.0</Version>` in the repo-root `Directory.Build.props` swept up `AutoNate.Plugin.Abstractions`. A plugin compiles against that assembly and ships **without** it (`Private=false`), so the host's copy defines type identity across the `AssemblyLoadContext` boundary — moving its version changes the identity every already-built third-party plugin's baked-in reference asks for. All of them would have stopped loading.

The symptom is what makes it worth writing down: enable returns 400 with `Type 'AutoNate.Plugins.HelloPlugin.HelloPlugin' not found in 'HelloPlugin.dll'`, which reads as a badly-built plugin rather than a binding failure. The stale sample-plugin zip looked like the obvious culprit.

`AssemblyVersion` is now pinned to `1.0.0.0` — the SDK default this assembly has always had, so the pin restores the exact prior identity while `FileVersion` and `InformationalVersion` still track the product. Verified by rebuilding the sample plugin from *pre-change* sources and loading that binary against the new host; red-checked by removing the pin. `PluginAbiVersionTests` guards it.

CLAUDE.md already listed the plugin ABI among the identifiers that must not be renamed. Its version is the same invariant, and the plan did not name it.

## Verification

Full `AutoNate.Web.Tests` **1662 / 0** and full `AutoNate.E2E.Tests` **164 / 0** — the whole E2E suite, including the Flowable and Dapr specs CI filters out, because the local compose stack has both. SPA lint, `tsc -b` and `npm run build` clean; the built `dist/index.html` carries `<title>Auton8</title>`.

Two E2E specs asserted the old `Automation Dashboard` heading and now assert `Home` by role and level; six agent specs follow the new label.

One local-only trap, not a regression: running E2E before the backend suite empties `AutoNate.Web/wwwroot` and leaves stale static-web-asset manifests, which fails ~50 backend tests on the *next* run. Clearing the manifest caches and rebuilding with `-p:BuildSpa=true` recovers it. CI is unaffected — separate jobs.

## Still outstanding before the flip

1. **The history rewrite is not in this PR.** `src/AutoNate.Web/ColorAdmin/` — a paid ThemeForest theme — is still reachable in history. Verified since the plan was written: the path list needed widening, because `src/AutoNate.Spa/src/scss/` holds 402 more objects of the same theme's SCSS that stripping only `ColorAdmin/` would have left behind. All six paths have **zero** tracked files at HEAD, so the rewrite is content-neutral and `git diff <old-master> HEAD` must come back empty.
2. **The security-issue gate is clear.** 26 closed `security` issues become world-readable, and all 26 are closed as *completed* — none wontfix, none deferred, and **zero open**. Publishing them reads as diligence.
3. `.n8/config.yml` still says `security_findings: issues`, whose stated rationale ("private repo → issues are already maintainer-only") expires at the flip.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Y5ie3qTEptr4MjYw5i6a5F

---

## archived-195 — Document why a force-push does not remove history from GitHub

`OPEN` · nathanpond · opened 2026-09-02 · `fix/rewrite-pull-refs-caveat` → `master`

The rewrite ran clean — 130 MiB → 8.63 MiB, zero ColorAdmin objects on every branch and tag, and `master^{tree}` identical before and after. It still did not achieve its purpose.

GitHub keeps a read-only `refs/pull/<n>/head` for every pull request ever opened. They are server-managed: `filter-repo` cannot rewrite them and they cannot be deleted. After the force-push they still pointed at the original commits, so the stripped theme was fetchable by anyone who could read the repository:

```
git fetch origin 'refs/pull/194/head:refs/pr194'
git rev-list --objects refs/pr194 | grep -ci coloradmin   # 21922
```

73 pull refs carry it. The repository was made public and reverted to private within minutes on discovering this; exposure was a few minutes at 0 forks and 0 stars.

This PR does not fix the exposure — it records it, and turns the check into a gate. The runbook now leads with the caveat and adds the pull-ref verification that must pass **before** flipping visibility.

Going public stays blocked until GitHub purges the pre-rewrite objects. The rewrite was not wasted: branches and tags are clean and it is a precondition for either remedy.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Y5ie3qTEptr4MjYw5i6a5F

---

