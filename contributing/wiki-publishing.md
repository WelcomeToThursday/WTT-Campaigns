# Editing documentation

All documentation is maintained in this source repository. Edit the Markdown files in `wiki/`, then commit and push them with the rest of the project. No separate repository, synchronization workflow or publishing credential is required.

Readers open **Documentation home** from the main README, or browse the `wiki/` folder and follow its README to `Home.md`. GitHub displays these files in the repository's **Code** tab. The GitHub **Wiki** tab is a separate service and is not used for these guides.

## Maintaining pages

- Keep `Home.md` as the complete guide index and `README.md` as the folder entry point.
- Use relative Markdown links with `.md` extensions, such as `[Characters](characters.md)` or `[Documentation home](Home.md)`. Keep section anchors after the extension.
- Keep supporting files in `examples/`, including the story overlay JSON.
- Update `Home.md` and `_Sidebar.md` when adding or renaming a guide. `_Sidebar.md` and `_Footer.md` are ordinary Markdown files here; GitHub does not automatically insert them into repository pages. Each guide has an explicit return link to the home page and navigation.
- Check that linked files and section headings exist before committing.

Release packaging includes the `wiki/` snapshot alongside the README and release notes. Relative links also work in a local Markdown viewer. Documentation-only changes do not alter installed runtime files or require a game/server restart.
