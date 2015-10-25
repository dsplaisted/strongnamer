using Octokit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestLibrary
{
    public class Class1
    {
        public async Task Test()
        {
            var github = new GitHubClient(new ProductHeaderValue("MyAmazingApp"));
            var user = await github.User.Get("half-ogre");
            Console.WriteLine(user.Followers + " folks love the half ogre!");
        }
    }
}
