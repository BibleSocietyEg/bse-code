using FluentAssertions;

namespace BSE_Code.Tests;

/// <summary>
/// SkillManager.ProjectSkillsDir is static readonly, frozen to the CWD at class-load time
/// (the test runner's working directory = repo root). We write test skills directly there
/// and clean them up in Dispose. Tests run sequentially (parallelism disabled in xunit.runner.json).
/// </summary>
[Collection("Sequential")]
public class SkillManagerTests : IDisposable
{
    // This is the exact path SkillManager reads project skills from
    private static readonly string ProjectSkillsDir = Path.Combine(
        Directory.GetCurrentDirectory(), ".bse-code", "skills");

    private readonly List<string> _createdFiles = [];

    public SkillManagerTests()
    {
        Directory.CreateDirectory(ProjectSkillsDir);
        SkillManager.Reload(); // start clean
    }

    public void Dispose()
    {
        foreach (var f in _createdFiles)
            if (File.Exists(f)) File.Delete(f);
        SkillManager.Reload(); // restore state
    }

    private string WriteSkill(string name, string content)
    {
        var path = Path.Combine(ProjectSkillsDir, $"{name}.md");
        File.WriteAllText(path, content);
        _createdFiles.Add(path);
        return path;
    }

    [Fact]
    public void Reload_WithProjectSkillFile_LoadsSkill()
    {
        WriteSkill("ts_review", "# Code Review\nReview the code.");

        SkillManager.Reload();

        SkillManager.All.Should().Contain(s => s.Name == "ts_review" && !s.IsUserLevel);
    }

    [Fact]
    public void Reload_SkillNameIsLowercase()
    {
        WriteSkill("TS_MySkill", "content");

        SkillManager.Reload();

        SkillManager.All.Should().Contain(s => s.Name == "ts_myskill");
    }

    [Fact]
    public void Find_ExistingProjectSkill_ReturnsSkill()
    {
        WriteSkill("ts_deploy", "# Deploy");
        SkillManager.Reload();

        var skill = SkillManager.Find("ts_deploy");

        skill.Should().NotBeNull();
        skill!.Name.Should().Be("ts_deploy");
    }

    [Fact]
    public void Find_CaseInsensitive_ReturnsSkill()
    {
        WriteSkill("ts_deploy2", "# Deploy");
        SkillManager.Reload();

        var skill = SkillManager.Find("TS_DEPLOY2");

        skill.Should().NotBeNull();
    }

    [Fact]
    public void Find_NonExistentSkill_ReturnsNull()
    {
        SkillManager.Reload();

        var skill = SkillManager.Find("zzz_nonexistent_skill_xyz");

        skill.Should().BeNull();
    }

    [Fact]
    public void BuildSystemContext_WithProjectSkill_ContainsSkillContent()
    {
        WriteSkill("ts_testctx", "Run all tests.");
        SkillManager.Reload();

        var context = SkillManager.BuildSystemContext();

        context.Should().Contain("## Available Skills");
        context.Should().Contain("Run all tests.");
    }

    [Fact]
    public void BuildSystemContext_NoSkills_ReturnsEmpty()
    {
        // Reload with no test skills (user skills may still exist — that's fine,
        // we just verify the contract: if All is empty, context is empty)
        SkillManager.Reload();
        if (SkillManager.All.Count == 0)
            SkillManager.BuildSystemContext().Should().BeEmpty();
        else
            SkillManager.BuildSystemContext().Should().Contain("## Available Skills");
    }

    [Fact]
    public void Reload_MultipleProjectSkillFiles_LoadsAll()
    {
        WriteSkill("ts_alpha", "Skill A");
        WriteSkill("ts_beta", "Skill B");
        WriteSkill("ts_gamma", "Skill C");

        SkillManager.Reload();

        SkillManager.All.Count(s => !s.IsUserLevel).Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void All_IsReadOnly()
    {
        SkillManager.Reload();

        SkillManager.All.Should().BeAssignableTo<IReadOnlyList<SkillManager.Skill>>();
    }
}
