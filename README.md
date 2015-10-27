# Strong Namer

Most applications in .NET [do not need to be strong named][1].  Strong names can
also introduce pain because they end up requiring binding redirects.  Because of
this, many OSS libraries do not strong name their assemblies.

[1]: https://github.com/dotnet/corefx/blob/c02d33b18398199f6acc17d375dab154e9a1df66/Documentation/project-docs/strong-name-signing.md#faq

Strong named assemblies cannot reference assemblies that aren't strong named.
So if for whatever reason you actually do need to strong name your project, you
couldn't easily consume open source packages.

Strong Namer is a NuGet package which aims to change this.  Simply install the
[StrongNamer](https://www.nuget.org/packages/strongnamer) NuGet package, and
it will transparently and automatically sign the assemblies you reference as
part of the build process.
