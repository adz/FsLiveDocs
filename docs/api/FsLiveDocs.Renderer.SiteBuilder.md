# SiteBuilder

`SiteBuilder` renders guide pages, API pages, the API index, version navigation, and `llms.txt`.

Use `build` for one resolved site. Use `buildHistory` for a set of verified release sites.

`buildHistory` renders the current version at the output root and older versions below `history/<version>/`.

Callers provide renderer-neutral package and content models. `SiteBuilder` owns final HTML and generated-link validation.
