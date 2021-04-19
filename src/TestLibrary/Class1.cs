using Octokit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestLibrary
{
    /// <summary>
    /// a test comment 
    /// </summary>
    public class Class1
    {
        /// <summary>
        /// a test method 
        /// </summary>
        public async Task TestAsync()
        {
            var github = new GitHubClient(new ProductHeaderValue("MyAmazingApp"));
            var user = await github.User.Get("half-ogre");
            Console.WriteLine(user.Followers + " folks love the half ogre!");
        }
    }
}
