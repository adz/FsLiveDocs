namespace FsLiveDocs.Core.Schema

open Reified
open Reified.SchemaDSL
open FsLiveDocs.Core

/// Reified schemas for the site-shell configuration and page frontmatter persisted inside a
/// `ReleaseContentArtifact`. Wire-compatible with the pre-Reified Newtonsoft format: see
/// `DocumentationSchema` for the shared compatibility rules.
module SiteSchema =

    let navigationItem : Schema<NavigationItem> =
        schema<NavigationItem> {
            fieldAs "Label" (fun (n: NavigationItem) -> n.Label) { withSchema Schema.text }
            fieldAs "Href" (fun (n: NavigationItem) -> n.Href) { withSchema Schema.text }
            construct (fun label href -> { Label = label; Href = href })
        }

    let siteConfig : Schema<SiteConfig> =
        schema<SiteConfig> {
            fieldAs "RepoUrl" (fun (s: SiteConfig) -> s.RepoUrl) { withSchema (Schema.option Schema.text) }
            fieldAs "SiteName" (fun (s: SiteConfig) -> s.SiteName) { withSchema (Schema.option Schema.text) }
            fieldAs "LogoText" (fun (s: SiteConfig) -> s.LogoText) { withSchema (Schema.option Schema.text) }
            fieldAs "LogoPath" (fun (s: SiteConfig) -> s.LogoPath) { withSchema (Schema.option Schema.text) }
            fieldAs "LogoDarkPath" (fun (s: SiteConfig) -> s.LogoDarkPath) { withSchema (Schema.option Schema.text) }
            fieldAs "ShowSiteName" (fun (s: SiteConfig) -> s.ShowSiteName) { withSchema (Schema.option Schema.bool) }
            fieldAs "Stylesheet" (fun (s: SiteConfig) -> s.Stylesheet) { withSchema (Schema.option Schema.text) }
            fieldAs "Themes" (fun (s: SiteConfig) -> s.Themes) { withSchema (Schema.option (Schema.listWith Schema.text)) }
            fieldAs "Navigation" (fun (s: SiteConfig) -> s.Navigation) { withSchema (Schema.option (Schema.listWith navigationItem)) }
            fieldAs "FSharpPrelude" (fun (s: SiteConfig) -> s.FSharpPrelude) { withSchema (Schema.option Schema.text) }
            construct (fun repoUrl siteName logoText logoPath logoDarkPath showSiteName stylesheet themes navigation fsharpPrelude ->
                { RepoUrl = repoUrl
                  SiteName = siteName
                  LogoText = logoText
                  LogoPath = logoPath
                  LogoDarkPath = logoDarkPath
                  ShowSiteName = showSiteName
                  Stylesheet = stylesheet
                  Themes = themes
                  Navigation = navigation
                  FSharpPrelude = fsharpPrelude })
        }

    let contentMetadata : Schema<ContentMetadata> =
        schema<ContentMetadata> {
            fieldAs "Title" (fun (m: ContentMetadata) -> m.Title) { withSchema Schema.text }
            fieldAs "Type" (fun (m: ContentMetadata) -> m.Type) { withSchema (Schema.option Schema.text) }
            fieldAs "Project" (fun (m: ContentMetadata) -> m.Project) { withSchema (Schema.option Schema.text) }
            fieldAs "TargetFramework" (fun (m: ContentMetadata) -> m.TargetFramework) { withSchema (Schema.option Schema.text) }
            fieldAs "Platform" (fun (m: ContentMetadata) -> m.Platform) { withSchema (Schema.option Schema.text) }
            construct (fun title type_ project targetFramework platform ->
                { Title = title; Type = type_; Project = project; TargetFramework = targetFramework; Platform = platform })
        }
