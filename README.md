# codiff-helper

Open a GitHub pull request for review in [Codiff](https://github.com/nkzw-tech/codiff) with one command, and clean up
the local copy when you are done.

> **Unofficial.** codiff-helper is an independent companion to Codiff. It is not made, endorsed or supported by
> Codiff's authors.

Codiff reviews a pull request from inside a local clone of its repository. codiff-helper does the repetitive part
for you:

```sh
codiff-helper https://github.com/acme/widgets/pull/42          # clone, check out, open in Codiff
codiff-helper https://github.com/acme/widgets/pull/42 --done   # delete the local copy
```

Each pull request gets its own folder, `~/Reviews/<owner>/<repo>/pr-<number>`, made with a fast partial clone and
switched to the pull request's latest commits. Codiff then opens on it with the AI walkthrough started. Codiff's
own "open in editor" action takes you to your editor from there.

## Requirements

- macOS on Apple silicon.
- [GitHub CLI](https://cli.github.com) (`gh`), signed in with `gh auth login`. codiff-helper uses it for all GitHub
  access, so private repositories and pull requests from forks work with your existing login.
- [Codiff](https://github.com/nkzw-tech/codiff) installed, with its `codiff` command on your `PATH`.
- To build: the [.NET 10 SDK](https://dotnet.microsoft.com/download) and the Xcode command line tools
  (`xcode-select --install`).

## Install

```sh
git clone https://github.com/lbildzinkas/codiff-helper.git
cd codiff-helper
./install.sh
```

`install.sh` builds a single native program and copies it to `~/.local/bin/codiff-helper`. Make sure `~/.local/bin`
is on your `PATH`. Run it again at any time to rebuild and replace the installed copy. Set
`CODIFF_HELPER_INSTALL_DIR` to install somewhere else.

## Commands

### Open a pull request

```sh
codiff-helper https://github.com/acme/widgets/pull/42
```

- The first time, it clones the repository into `~/Reviews/acme/widgets/pr-42`, checks out the pull request's
  latest commits on a local branch named `pr-42`, and opens Codiff with the walkthrough started.
- Running it again updates the copy to the pull request's latest commits and opens it. If you have edited files
  or made commits in the copy, it is left exactly as it is and a warning says so.
- Merged and closed pull requests open too, with a one-line note.
- URLs copied from any pull request tab work: `/files`, `/commits`, a `?query` or a `#fragment` are ignored.

Options:

```sh
codiff-helper https://github.com/acme/widgets/pull/42 --no-walkthrough   # open without the AI walkthrough
codiff-helper https://github.com/acme/widgets/pull/42 --agent pi         # pass an agent backend to Codiff
```

### Delete a copy

```sh
codiff-helper https://github.com/acme/widgets/pull/42 --done
```

Inside a review folder, the URL can be left out:

```sh
cd ~/Reviews/acme/widgets/pr-42
codiff-helper --done
```

If your shell was inside the folder that was deleted, codiff-helper prints the `cd` to run next.

### List copies

```sh
codiff-helper --list
```

```text
REVIEW           STATE   LOCAL          TITLE              PATH
acme/api#21      merged  local changes  Speed up search    ~/Reviews/acme/api/pr-21
acme/widgets#42  open    clean          Add widget sizes   ~/Reviews/acme/widgets/pr-42
```

Titles and states come from GitHub. When GitHub cannot be reached, the ones saved at the last open are shown.

### Help and version

```sh
codiff-helper --help
codiff-helper --version
```

## What `--done` will and will not delete

`--done` is careful:

- It only deletes folders that codiff-helper created. Each one carries a small ownership record inside its `.git`
  directory, so the record never shows up as a change. Folders without a valid record are never deleted, even
  with `--force`.
- It only deletes a folder at exactly `<reviews root>/<owner>/<repo>/pr-<number>`. Paths are resolved first, and
  anything that reaches outside the reviews root through a symlink or `..` is refused, even with `--force`. The
  reviews root itself is never deleted. Empty `<owner>` and `<repo>` folders left behind are tidied away.
- It refuses when the copy holds anything that is not on GitHub, and lists what is in the way:
  - changed files (staged or not),
  - untracked files,
  - commits on any local branch, or on a detached HEAD, that are not on GitHub,
  - stashed changes.

  Push or discard that work, or add `--force` to delete the copy anyway:

  ```sh
  codiff-helper https://github.com/acme/widgets/pull/42 --done --force
  ```

There is no option to delete every copy at once.

## Settings

| Variable | Default | Meaning |
| --- | --- | --- |
| `CODIFF_HELPER_ROOT` | `~/Reviews` | Folder that holds the review copies. Must be an absolute path; a leading `~` means your home folder. |
| `CODIFF_HELPER_INSTALL_DIR` | `~/.local/bin` | Where `install.sh` puts the program. |

Owner and repository folder names are always lower case, so URLs that differ only in case share one copy.

## Troubleshooting

Each problem is reported on one line, and the exit code is non-zero:

- `the GitHub CLI (gh) is not installed` means you should install it (`brew install gh`), then run `gh auth login`.
- `gh is not signed in to GitHub` means you should run `gh auth login`.
- `the codiff command was not found` means you should install Codiff and make sure its `codiff` command is on your
  `PATH`.

## Development

```sh
dotnet test
```

The tests use fakes for `gh`, `git` and `codiff`, plus local temporary Git repositories. They never touch your real
reviews folder and never launch Codiff. An extra live test clones a small public pull request into a temporary
folder; it runs only when asked:

```sh
CODIFF_HELPER_LIVE_TESTS=1 dotnet test
```

## License

[MIT](LICENSE)
