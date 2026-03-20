using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Theatre.Transport;

namespace Theatre.Tests.Editor
{
    [TestFixture]
    public class ToolRegistryTests
    {
        private ToolRegistry _registry;
        private JToken _emptySchema;

        [SetUp]
        public void SetUp()
        {
            _registry = new ToolRegistry();
            _emptySchema = JToken.Parse(
                @"{""type"":""object"",""properties"":{}}");
        }

        [Test]
        public void RegisterAndCount()
        {
            _registry.Register(MakeTool("test", ToolGroup.StageGameObject));
            Assert.AreEqual(1, _registry.Count);
        }

        [Test]
        public void ListToolsFiltersbyGroup()
        {
            _registry.Register(MakeTool("stage_tool", ToolGroup.StageGameObject));
            _registry.Register(MakeTool("ecs_tool", ToolGroup.ECSWorld));

            var stageOnly = _registry.ListTools(ToolGroup.StageGameObject);
            Assert.AreEqual(1, stageOnly.Count);
            Assert.AreEqual("stage_tool", stageOnly[0].Name);

            var all = _registry.ListTools(ToolGroup.Everything);
            Assert.AreEqual(2, all.Count);
        }

        [Test]
        public void ListToolsExcludesDisabled()
        {
            _registry.Register(MakeTool("tool_a", ToolGroup.StageGameObject));
            _registry.Register(MakeTool("tool_b", ToolGroup.StageGameObject));

            var disabled = new HashSet<string> { "tool_b" };
            var list = _registry.ListTools(ToolGroup.StageGameObject, disabled);

            Assert.AreEqual(1, list.Count);
            Assert.AreEqual("tool_a", list[0].Name);
        }

        [Test]
        public void GetToolReturnsNullForDisabledGroup()
        {
            _registry.Register(MakeTool("ecs_tool", ToolGroup.ECSWorld));

            var result = _registry.GetTool("ecs_tool", ToolGroup.StageGameObject);
            Assert.IsNull(result);
        }

        [Test]
        public void GetToolReturnsNullForDisabledTool()
        {
            _registry.Register(MakeTool("tool_a", ToolGroup.StageGameObject));

            var disabled = new HashSet<string> { "tool_a" };
            var result = _registry.GetTool(
                "tool_a", ToolGroup.StageGameObject, disabled);
            Assert.IsNull(result);
        }

        [Test]
        public void GetToolReturnsToolWhenEnabled()
        {
            _registry.Register(MakeTool("tool_a", ToolGroup.StageGameObject));

            var result = _registry.GetTool("tool_a", ToolGroup.Everything);
            Assert.IsNotNull(result);
            Assert.AreEqual("tool_a", result.Name);
        }

        private ToolRegistration MakeTool(string name, ToolGroup group)
        {
            return new ToolRegistration(
                name, $"Description of {name}", _emptySchema, group,
                args => "{}");
        }
    }
}
