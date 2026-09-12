# Publishing the GitHub wiki

Player and campaign-author documentation lives in `wiki/`. `Home.md` is the landing page; `_Sidebar.md` and `_Footer.md` provide shared navigation. Keep page Markdown files at the top level and supporting files in `examples/`. Page links use the canonical `https://github.com/WelcomeToThursday/WTT-Campaigns/wiki/<page-name>` URL without `.md`.

GitHub hosts the wiki in a separate Git repository. Committing `wiki/` in the source repository does not publish it to the Wiki tab.

## First publication

An authenticated repository maintainer must open the [Wiki tab](https://github.com/WelcomeToThursday/WTT-Campaigns/wiki), choose **Create the first page**, and save a page named **Home**. This initializes the wiki Git repository. If the Wiki tab is unavailable, check the repository's wiki setting and your access.

## Publish reviewed pages

From the source checkout, clone the wiki into a new sibling directory, then copy the contents of `wiki/` into the clone. Use a fresh destination name if the example directory already exists.

```powershell
git clone https://github.com/WelcomeToThursday/WTT-Campaigns.wiki.git ../WTT-Campaigns.wiki
Copy-Item -Path ./wiki/* -Destination ../WTT-Campaigns.wiki -Recurse -Force
git -C ../WTT-Campaigns.wiki status --short
git -C ../WTT-Campaigns.wiki diff --check
git -C ../WTT-Campaigns.wiki diff
```

Review changes against any edits already made on GitHub. Copying preserves unrelated wiki pages; explicitly review obsolete pages when a guide is renamed or retired. The clone retains the previous committed versions of replaced files.

After review, commit and publish the copied files:

```powershell
git -C ../WTT-Campaigns.wiki add -- Home.md _Sidebar.md _Footer.md *.md examples
git -C ../WTT-Campaigns.wiki diff --cached --check
git -C ../WTT-Campaigns.wiki diff --cached
git -C ../WTT-Campaigns.wiki commit -m "Move campaign guides to GitHub wiki"
git -C ../WTT-Campaigns.wiki push origin HEAD
```

Open the published Home page and check its guide links, sidebar and the downloadable JSON example. Commit the corresponding source changes through the normal repository review workflow so future edits start from the published content.

Release packaging includes the `wiki/` snapshot alongside the README and release notes. Wiki-only changes do not alter installed runtime files or require a game/server restart.
