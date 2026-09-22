# A GitHub token for reading a core

The Core updater can read a project's core library straight out of a GitHub repository. This page is how you give it the access it needs — and no more than that.

**The token this framework wants can do exactly one thing: read the files of one repository.** It cannot push, cannot open an issue, cannot touch any other repository you have, and it stops working on a date you choose. Five minutes to make, and nothing on this page needs an administrator.

## Do you need one at all?

| Your core's repository is | What you need |
| --- | --- |
| **Public** | nothing. It is read without a token |
| **Private** | a token — GitHub answers *"not found"* to anyone reading a private repository without one |

There is one reason to make a token for a **public** repository too: without one GitHub allows **sixty API calls an hour** from your address, shared by everybody behind it. A **Load** costs three, and a download two more plus one for each source it brings, so one engineer rarely meets the limit — an office sharing one address does, and sooner than it expects. With a token it is 5,000 an hour, per token.

## Make the token

GitHub has two kinds. You want a **fine-grained** one, which is the kind that can be narrowed to a single repository. (The older "classic" token cannot: its `repo` scope is read **and write** on **every** repository you can reach. Use it only if your organization has blocked the fine-grained kind.)

1. On GitHub, click your profile picture, top right → **Settings**.
2. In the left sidebar, at the very bottom → **Developer settings**.
3. Under *Personal access tokens* → **Fine-grained tokens**.
4. **Generate new token**.
5. **Token name** — something you will recognise in a year: `PLC-Framework core read`.
6. **Resource owner** — you, or the organization that owns the repository. Pick the owner of the *core* repository. An organization that has blocked fine-grained tokens does not appear in this list; see below.
7. **Expiration** — up to **366 days**, and your organization may cap it lower. Pick a date you will still be on this project, and put a reminder in your calendar: when it passes, the Core updater simply stops being able to read the core.
8. **Repository access** → **Only select repositories** → in **Select repositories**, choose the one holding the core, and only that one.
9. **Permissions** → **Repository permissions** → find **Contents** and set it to **Read-only**.
    - **Metadata: Read-only** appears by itself, marked *mandatory*. Leave it. GitHub adds it to any token that has a repository permission at all.
    - Set nothing else. *Contents* is what covers the three calls this framework makes: which commit a branch is at, what files are in a folder, and one file's bytes.
10. **Generate token**.
11. **Copy it now.** It starts with `github_pat_` and GitHub never shows it again. If you lose it, you delete it and make another — there is no way to read it back.

> **If the repository belongs to an organization**, the token may have to be approved by an owner before it works, and the *Generate* page will ask you for a justification. Until somebody approves it, the token exists and is refused. If the organization does not appear under *Resource owner* at all, it has blocked fine-grained tokens, and an owner has to enable them or you have to fall back to a classic token.

## Give it to the framework

**Never put the token in `config.json`.** That file lives inside the TIA project and TIA projects are under version control, so a token written there reaches a commit, a backup or a colleague's clone sooner or later. `config.json` carries only the *name* of the variable that holds it:

```jsonc
"coreRemoteRepositoryConfig": {
  "provider": "github",
  "apiUrl":   "https://api.github.com/",
  "owner":    "your-org",
  "repository": "code",
  "branch":   "main",
  "folder":   "plc/s7-1x00/core",
  "dependencyFile": "core.json",
  "token":    "${REPO_TOKEN}"
}
```

The value goes in a file of your own, outside any project:

```
%LOCALAPPDATA%\PLC-Framework\.env
```

**The easy way is to let the Config. Editor do it.** Open it from the TIA menu on the project root, go to **Repository**, paste the token into the **Token** field — it is masked, with an eye to check what you pasted — and **Save**. The editor writes the `.env` first and the JSON after, so it never leaves a `config.json` pointing at a variable nobody managed to set.

By hand, the file is one line:

```
REPO_TOKEN=github_pat_...
```

That file is **per Windows user**. Another engineer on the same station has their own, and none of it travels with the project, which is the point.

## When it goes wrong

The Core updater says what happened rather than "failed". These are the four you are likely to meet:

| What you see | What it means |
| --- | --- |
| *"GitHub answered 'not found' for `owner/repo@branch`, which is also what it answers for a private repository read without a token. Set REPO_TOKEN, or check the name."* | no token reached the framework — or the owner, repository or branch is misspelt. GitHub deliberately does not distinguish the two |
| *"GitHub refused the token."* | the token is wrong, was deleted, or has expired |
| *"GitHub refused the request for `owner/repo@branch`, which usually means the token cannot read it."* | the token is valid but does not cover **this** repository: it was made for a different one, or it is waiting for an organization owner to approve it |
| *"GitHub's rate limit has been reached — and without a token it is sixty calls an hour."* | you are reading without a token. Wait, or make one |
| *"GitHub says `owner/repo@branch` has moved, and is now `new-owner/repo`."* | somebody renamed the repository or its owner. Put the new name in `config.json` — the framework does not follow the move by itself, because then the file would stay wrong for ever |

Two things worth knowing when you are hunting one of these down:

- **An unresolved `${REPO_TOKEN}` is read as "no token", not as an empty one.** If the `.env` has no such line, the framework behaves exactly as it does for a public repository — which is why a private repository then answers *not found* rather than *unauthorized*.
- **The framework only ever reads.** Nothing it does with this token can change the repository, so a token that works is a token that is safe to leave in place until it expires.

## Replacing or revoking it

Same page — **Settings → Developer settings → Fine-grained tokens**. A token can be deleted there at any moment and stops working immediately; the Core updater then reports the refusal above and the project is otherwise untouched, since the core it already copied is inside the project.

To rotate one, make the new token first, paste it into the Config. Editor, check that the Core updater still reads the core, and delete the old one afterwards.

---

Sources for the GitHub side of this page, read in September 2026:

- [Managing your personal access tokens — GitHub Docs](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/managing-your-personal-access-tokens)
- [Permissions required for fine-grained personal access tokens — GitHub Docs](https://docs.github.com/en/rest/authentication/permissions-required-for-fine-grained-personal-access-tokens)
- [Introducing fine-grained personal access tokens for GitHub — The GitHub Blog](https://github.blog/security/application-security/introducing-fine-grained-personal-access-tokens-for-github/)
