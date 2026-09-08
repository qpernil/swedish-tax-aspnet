# GitHub Pages hosting

The [live calculator](https://qpernil.github.io/swedish-tax-aspnet/) is a static
Blazor WebAssembly application. GitHub serves `index.html`, styles, scripts,
.NET assemblies and the combined .NET/Rust native WebAssembly runtime.
There is no ASP.NET process or calculation endpoint on GitHub Pages.

## CI owns the deployed artifact

`.github/workflows/ci.yml` builds the pinned Rust library and runs the native,
.NET and browser verification suites. It then publishes the client with
`StaticSite=true`, sets the repository base path, and tests the actual static
artifact under that path in Chromium, Firefox and WebKit. Pull requests run
these checks without deploying.

For a successful `master` build, `actions/upload-pages-artifact` uploads that
same directory. The dependent deployment job uses `actions/deploy-pages` to
publish it to the `github-pages` environment. Deployment does not rebuild the
site. Generated site files are neither committed nor uploaded from a developer's
machine. A manual workflow run on `master` also builds, tests and deploys.

The repository's **Settings → Pages → Build and deployment → Source** must be
**GitHub Actions**. The workflow has Pages and OIDC write permissions only in
its deployment job; build jobs have read-only repository access.

## Shared application, two entry points

The ordinary build retains the ASP.NET host and its generated `App.razor`
entry page. With `StaticSite=true`, the client registers `Routes` and
`HeadOutlet` as root components, includes `wwwroot/index.html`, and publishes
its scoped styles plus the host's shared `app.css`. Both modes use the same
UI source, services and Rust/P/Invoke calculation path.

`scripts/publish-pages.py` sets `<base href="/swedish-tax-aspnet/">` for the
project site. It includes `.nojekyll` and a `404.html` shell so missing routes
can show the Blazor not-found page with assets resolved from the correct base.
Assets are uploaded directly from the publish output, preserving their hashes.
Uncompressed runtime files remain available; custom Brotli decoding is not
required. The app does not claim offline navigation or PWA installation.

Saved plans belong to the site's browser origin. The GitHub Pages site does
not inherit the workspace saved on localhost. GitHub project sites under the
same account share an origin; local storage is not an account synchronization
or confidential server-side store.

## Local static preview

First build the pinned Rust library using the root README instructions. Then:

```sh
python3 scripts/publish-pages.py --output /tmp/tax-pages-preview/swedish-tax-aspnet
python3 -m http.server 5183 --bind 127.0.0.1 --directory /tmp/tax-pages-preview
```

Open `http://127.0.0.1:5183/swedish-tax-aspnet/`. The script requires a fresh
output directory to avoid retaining stale assets. `--dotnet /path/to/dotnet`
selects an isolated SDK; `--base-path /` supports a root-hosted preview.
This command only creates a local artifact; CI supplies the public deployment.

```sh
TAX_APP_URL=http://127.0.0.1:5183/swedish-tax-aspnet/ pnpm test:browser
```

See [Microsoft's Blazor Pages guide](https://learn.microsoft.com/en-us/aspnet/core/blazor/host-and-deploy/webassembly/github-pages?view=aspnetcore-10.0)
and [GitHub's artifact deployment workflow](https://docs.github.com/en/pages/getting-started-with-github-pages/using-custom-workflows-with-github-pages).
