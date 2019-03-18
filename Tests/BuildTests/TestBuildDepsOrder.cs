using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Common;
using Common.YamlParsers;
using log4net;
using NUnit.Framework;
using Tests.Helpers;

namespace Tests.BuildTests
{
    [TestFixture]
    public class TestBuildDepsOrder
    {
        private static readonly ILog Log = LogManager.GetLogger("TestBuildDepsOrder");

        [Test]
        public void TestBuildOrderCycle()
        {
            var graph = new Dictionary<Dep, List<Dep>>
            {
                {new Dep("A/full-build"), new List<Dep> {new Dep("B/full-build")}},
                {new Dep("B/full-build"), new List<Dep> {new Dep("C/full-build")}},
                {new Dep("C/full-build"), new List<Dep> {new Dep("D/full-build")}},
                {new Dep("D/full-build"), new List<Dep> {new Dep("A/full-build")}}
            };
            Assert.Throws<CementException>(() => BuildPreparer.GetOptimizedForParallelBuildOrder(graph));
            Assert.Throws<CementException>(() => BuildPreparer.GetTopologicallySortedGraph(graph, "A", "full-build"));
        }

        [Test]
        public void TestTopSortNoCycle()
        {
            var graph = new Dictionary<Dep, List<Dep>>
            {
                {new Dep("A/full-build"), new List<Dep> {new Dep("B/full-build"), new Dep("C/client")}},
                {new Dep("B/full-build"), new List<Dep> {new Dep("D/full-build"), new Dep("E/full-build")}},
                {new Dep("C/full-build"), new List<Dep> {new Dep("D/full-build")}},
                {new Dep("C/client"), new List<Dep> {new Dep("D/client")}},
                {new Dep("D/full-build"), new List<Dep> {new Dep("E/full-build")}},
                {new Dep("D/client"), new List<Dep>()},
                {new Dep("E/full-build"), new List<Dep>()}
            };
            Assert.AreEqual(new[]
            {
                new Dep("E/full-build"),
                new Dep("D/full-build"),
                new Dep("B/full-build"),
                new Dep("D/client"),
                new Dep("C/client"),
                new Dep("A/full-build")
            }, BuildPreparer.GetTopologicallySortedGraph(graph, "A", "full-build").ToArray());
        }

        [Test]
        public void TestBuildOrderForParallel()
        {

            var graph = new Dictionary<Dep, List<Dep>>
            {
                {new Dep("A/full-build"), new List<Dep> {new Dep("B/full-build"), new Dep("C/client"), new Dep("H/full-build") }},
                {new Dep("B/full-build"), new List<Dep> {new Dep("D/full-build"), new Dep("E/full-build")}},
                {new Dep("C/client"), new List<Dep> {new Dep("D/client"), new Dep("F/full-build")}},
                {new Dep("D/full-build"), new List<Dep> {new Dep("E/full-build"), new Dep("G/full-build")}},
                {new Dep("D/client"), new List<Dep>()},
                {new Dep("E/full-build"), new List<Dep>()},
                {new Dep("F/full-build"), new List<Dep>()},
                {new Dep("G/full-build"), new List<Dep>()},
                {new Dep("H/full-build"), new List<Dep> {new Dep("F/full-build"), new Dep("G/full-build") }},
            };
            var sortedGraph = BuildPreparer.GetTopologicallySortedGraph(graph, "A", "full-build");

            Assert.AreEqual(new[]
            {
                new Dep("E/full-build"),
                new Dep("G/full-build"),
                new Dep("D/full-build"),
                new Dep("B/full-build"),
                new Dep("D/client"),
                new Dep("F/full-build"),
                new Dep("C/client"),
                new Dep("H/full-build"),
                new Dep("A/full-build")
            }, sortedGraph);

            var optimizedBuildOrder = BuildPreparer.GetOptimizedForParallelBuildOrder(graph).ToArray();
            Assert.AreEqual(new[]
            {
                new Dep("D/client"),
                new Dep("E/full-build"),
                new Dep("F/full-build"),
                new Dep("G/full-build"),
                new Dep("C/client"),
                new Dep("D/full-build"),
                new Dep("H/full-build"),
                new Dep("B/full-build"),
                new Dep("A/full-build")
            }, optimizedBuildOrder);
        }

        [Test]
        public void TestConfigGraph()
        {
            using (var env = new TestEnvironment())
            {
                env.CreateRepo("A", new Dictionary<string, DepsContent>
                {
                    {"full-build *default", new DepsContent(null, new List<Dep> {new Dep("B"), new Dep("C", null, "client")})}
                });
                env.CreateRepo("B", new Dictionary<string, DepsContent>
                {
                    {"full-build *default", new DepsContent(null, new List<Dep> {new Dep("D")})}
                });
                env.CreateRepo("C", new Dictionary<string, DepsContent>
                {
                    {"full-build *default", new DepsContent(null, new List<Dep> {new Dep("D")})},
                    {"client", new DepsContent(null, new List<Dep> {new Dep("D", null, "client")})}
                });
                env.CreateRepo("D", new Dictionary<string, DepsContent>
                {
                    {"full-build *default", new DepsContent(null, new List<Dep>())},
                    {"client", new DepsContent(null, new List<Dep>())}
                });
                Helper.SetWorkspace(env.RemoteWorkspace);
                var result = BuildPreparer.BuildConfigsGraph("A", null);
                Assert.AreEqual(new[] {new Dep("B", null, "full-build"), new Dep("C/client")}, result[new Dep("A", null, "full-build")].ToArray());
                Assert.AreEqual(new[] {new Dep("D", null, "full-build")}, result[new Dep("B", null, "full-build")].ToArray());
                Assert.AreEqual(new Dep[] { }, result[new Dep("D", null, "full-build")].ToArray());
                Assert.AreEqual(new[] {new Dep("D/client")}, result[new Dep("C/client")].ToArray());
                Assert.AreEqual(new string[] { }, result[new Dep("D/client")].ToArray());
            }
        }

        [Test]
        [TestCase(false), TestCase(true)]
        public void TestRelaxNesting(bool forParallel)
        {
            using (var env = new TestEnvironment())
            {
                env.CreateRepo("A", new Dictionary<string, DepsContent>
                {
                    {"full-build *default", new DepsContent(null, new List<Dep> {new Dep("B"), new Dep("C", null, "client")})}
                });
                env.CreateRepo("B", new Dictionary<string, DepsContent>
                {
                    {"full-build *default", new DepsContent(null, new List<Dep> {new Dep("C")})}
                });
                env.CreateRepo("C", new Dictionary<string, DepsContent>
                {
                    {"full-build > client *default", new DepsContent(null, new List<Dep>())},
                    {"client", new DepsContent(null, new List<Dep>())}
                });
                Helper.SetWorkspace(env.RemoteWorkspace);
                Directory.CreateDirectory(Path.Combine(env.RemoteWorkspace, ".cement"));

                var modulesOrder = new BuildPreparer(Log).GetModulesOrder("A", null, forParallel);
                Assert.IsFalse(modulesOrder.BuildOrder.Contains(new Dep("C/client")));
                Assert.IsTrue(modulesOrder.BuildOrder.Contains(new Dep("C", null, "full-build")));
                Assert.IsTrue(modulesOrder.BuildOrder.Contains(new Dep("A", null, "full-build")));
                Assert.IsTrue(modulesOrder.BuildOrder.Contains(new Dep("B", null, "full-build")));
                Assert.AreEqual(3, modulesOrder.BuildOrder.Count);
            }
        }

        [Test]
        [TestCase(false), TestCase(true)]
        public void TestNestingSkip(bool forParallel)
        {
            using (var env = new TestEnvironment())
            {
                env.CreateRepo("A", new Dictionary<string, DepsContent>
                {
                    {"full-build *default", new DepsContent(null, new List<Dep> {new Dep("B"), new Dep("C")})}
                });
                env.CreateRepo("B", new Dictionary<string, DepsContent>
                {
                    {"full-build *default", new DepsContent(null, new List<Dep> {new Dep("X", null, "client")})}
                });
                env.CreateRepo("C", new Dictionary<string, DepsContent>
                {
                    {"full-build *default", new DepsContent(null, new List<Dep> {new Dep("X"), new Dep("B")})}
                });
                env.CreateRepo("X", new Dictionary<string, DepsContent>
                {
                    {"full-build > client *default", new DepsContent(null, new List<Dep>())},
                    {"client", new DepsContent(null, new List<Dep>())}
                });

                Helper.SetWorkspace(env.RemoteWorkspace);
                Directory.CreateDirectory(Path.Combine(env.RemoteWorkspace, ".cement"));

                var modulesOrder = new BuildPreparer(Log).GetModulesOrder("A", null, forParallel);
                Assert.IsFalse(modulesOrder.BuildOrder.Contains(new Dep("X/client")));
                Assert.IsTrue(modulesOrder.BuildOrder.Contains(new Dep("A", null, "full-build")));
                Assert.IsTrue(modulesOrder.BuildOrder.Contains(new Dep("B", null, "full-build")));
                Assert.IsTrue(modulesOrder.BuildOrder.Contains(new Dep("C", null, "full-build")));
                Assert.AreEqual(4, modulesOrder.BuildOrder.Count);
                CollectionAssert.AreEqual(new List<Dep>
                {
                    new Dep("X", null, "full-build"),
                    new Dep("B", null, "full-build"),
                    new Dep("C", null, "full-build"),
                    new Dep("A", null, "full-build")
                }, modulesOrder.BuildOrder);
            }
        }

        [Test]
        [TestCase(false), TestCase(true)]
        public void TestNestingOnlyClient(bool forParallel)
        {
            using (var env = new TestEnvironment())
            {
                env.CreateRepo("A", new Dictionary<string, DepsContent>
                {
                    {"full-build *default", new DepsContent(null, new List<Dep> {new Dep("X", null, "client")})}
                });
                env.CreateRepo("X", new Dictionary<string, DepsContent>
                {
                    {"full-build > client *default", new DepsContent(null, new List<Dep>())},
                    {"client", new DepsContent(null, new List<Dep>())}
                });

                Helper.SetWorkspace(env.RemoteWorkspace);
                Directory.CreateDirectory(Path.Combine(env.RemoteWorkspace, ".cement"));

                var modulesOrder = new BuildPreparer(Log).GetModulesOrder("A", null, forParallel);
                Assert.IsFalse(modulesOrder.BuildOrder.Contains(new Dep("X", null, "full-build")));
                Assert.IsTrue(modulesOrder.BuildOrder.Contains(new Dep("X", null, "client")));
                Assert.IsTrue(modulesOrder.BuildOrder.Contains(new Dep("A", null, "full-build")));
                Assert.AreEqual(2, modulesOrder.BuildOrder.Count);
            }
        }

        [Test]
        [TestCase(false), TestCase(true)]
        public void TestNestingNeedBuildBoth(bool forParallel)
        {
            using (var env = new TestEnvironment())
            {
                env.CreateRepo("A", new Dictionary<string, DepsContent>
                {
                    {"client", new DepsContent(null, new List<Dep>())},
                    {"full-build > client *default", new DepsContent(null, new List<Dep> {new Dep("X")})}
                });
                env.CreateRepo("X", new Dictionary<string, DepsContent>
                {
                    {"full-build", new DepsContent(null, new List<Dep> {new Dep("A", null, "client")})}
                });

                Helper.SetWorkspace(env.RemoteWorkspace);
                Directory.CreateDirectory(Path.Combine(env.RemoteWorkspace, ".cement"));

                var modulesOrder = new BuildPreparer(Log).GetModulesOrder("A", null, forParallel);

                CollectionAssert.AreEqual(new List<Dep>
                {
                    new Dep("A", null, "client"),
                    new Dep("X", null, "full-build"),
                    new Dep("A", null, "full-build")
                }, modulesOrder.BuildOrder);
            }
        }

        [Test]
        [TestCase(false), TestCase(true)]
        public void TestNestingLotChildren(bool forParallel)
        {
            using (var env = new TestEnvironment())
            {
                env.CreateRepo("A", new Dictionary<string, DepsContent>
                {
                    {"full-build", new DepsContent(null, new List<Dep> {new Dep("C1"), new Dep("C2"), new Dep("C3"), new Dep("P1")})}
                });
                env.CreateRepo("C1", new Dictionary<string, DepsContent>
                    {{"full-build", new DepsContent(null, new List<Dep> {new Dep("X/child1")})}});
                env.CreateRepo("C2", new Dictionary<string, DepsContent>
                    {{"full-build", new DepsContent(null, new List<Dep> {new Dep("X/child2")})}});
                env.CreateRepo("C3", new Dictionary<string, DepsContent>
                    {{"full-build", new DepsContent(null, new List<Dep> {new Dep("X/child3")})}});
                env.CreateRepo("P1", new Dictionary<string, DepsContent>
                    {{"full-build", new DepsContent(null, new List<Dep> {new Dep("X/parent1")})}});
                env.CreateRepo("X", new Dictionary<string, DepsContent>
                {
                    {"child1", new DepsContent(null, new List<Dep>())},
                    {"child2 > child1", new DepsContent(null, new List<Dep>())},
                    {"child3 > child1", new DepsContent(null, new List<Dep>())},
                    {"parent1 > child1, child2", new DepsContent(null, new List<Dep>())},
                    {"parent2 > parent1, child3", new DepsContent(null, new List<Dep>())}
                });

                Helper.SetWorkspace(env.RemoteWorkspace);
                Directory.CreateDirectory(Path.Combine(env.RemoteWorkspace, ".cement"));

                var modulesOrder = new BuildPreparer(Log).GetModulesOrder("A", null, forParallel);

                var xDeps = modulesOrder.BuildOrder.Where(d => d.Name == "X").ToList();
                Assert.IsTrue(xDeps.Any(d => d.Name == "X" && d.Configuration == "parent1"));
                Assert.IsTrue(xDeps.Any(d => d.Name == "X" && d.Configuration == "child3"));
            }
        }
    }
}