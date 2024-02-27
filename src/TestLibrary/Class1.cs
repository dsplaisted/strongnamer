using System;
using System.Threading.Tasks;

namespace TestLibrary
{
	public class Class1
	{
		public async Task TestAsync()
		{
#if NET6_0_OR_GREATER
			Console.WriteLine($"{iTextSharp.text.pdf.PdfDocument.Product}");
			await Task.CompletedTask;
#else
            var github = new Octokit.GitHubClient(new Octokit.ProductHeaderValue("MyAmazingApp"));
            var user = await github.User.Get("half-ogre");
            Console.WriteLine(user.Followers + " folks love the half ogre!");
#endif
		}
	}
}
