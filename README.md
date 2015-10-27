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

# Demo

Here's how to try Strong Namer out for yourself:

- Create a new Console application
- Add a strong name to the application
  - *Go to the Signing tab of the project properties*
  - *Check "Sign the assembly"*
  - *In the key file dropdown, choose &lt;New...&gt;*
  - *Choose a key file name (ie "key.snk"), uncheck the password option,
     and click OK*
- Add a reference to the [Octokit](https://www.nuget.org/packages/octokit)
  NuGet package
- Replace Program class with the following code:

``` C#
using Octokit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

class Program
{
    static void Main(string[] args)
    {
        MainAsyncWithErrorHandling().Wait();
    }

    static async Task MainAsyncWithErrorHandling()
    {
        try
        {
            await MainAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }

    static async Task MainAsync()
    {
        var github = new GitHubClient(new ProductHeaderValue("MyAmazingApp"));
        var user = await github.User.Get("half-ogre");
        Console.WriteLine(user.Followers + " folks love the half ogre!");
    }
}
```
- Start without debugging (CTRL+F5)

![System.IO.FileLoadException: Could not load file or assembly 'Octokit, Version=0.16.0.0, Culture=neutral, PublicKeyToken=null' or one of its dependencies. A strongly-named assembly is required. (Exception from HRESULT: 0x80131044)
File name: 'Octokit, Version=0.16.0.0, Culture=neutral, PublicKeyToken=null'
   at Program.MainAsync()
   at Program.<MainAsyncWithErrorHandling>d__1.MoveNext() in C:\Users\daplaist\Documents\Visual Studio 2015\Projects\ConsoleApplication13\ConsoleApplication13\Program.cs:line 19](images/StrongNameSadPanda.png)
- :disappointed:
- Add a reference to the [StrongNamer](https://www.nuget.org/packages/strongnamer)
  NuGet package
- Start without debugging (CTRL+F5)

![75 folks love the half ogre!](images/StrongNameSuccessSparkles.png)
- :sparkles: :fireworks: :smile: :fireworks: :sparkles:
