using System.Data.Common;
using Xunit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ZupuPaiBan.Data;
using ZupuPaiBan.Models;
using ZupuPaiBan.ViewModels;

namespace PuJiang.Tests;

public class GenealogySelectionTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Target_shortcuts_resolve_selected_person_by_id_and_reject_missing_or_foreign_selection()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext);
        Assert.Contains("族谱", Assert.Throws<InvalidOperationException>(() => vm.GetSelectedTreePersonNode()).Message);
        vm.NewName = "甲谱";
        await vm.CreateAsync();
        var first = vm.SelectedGenealogy!;
        Assert.Contains("人物", Assert.Throws<InvalidOperationException>(() => vm.GetSelectedTreePersonNode()).Message);
        Person firstPerson, namesake;
        await using (var context = database.CreateContext())
        {
            var service = new GenealogyDataService(context);
            firstPerson = await service.AddPersonAsync(new Person { GenealogyId = first.Id, Name = "同名" });
            namesake = await service.AddPersonAsync(new Person { GenealogyId = first.Id, Name = "同名" });
        }
        await vm.SelectAsync(first);
        vm.SelectedPerson = vm.People.Single(p => p.Id == namesake.Id);
        var selectedNode = vm.GetSelectedTreePersonNode();
        Assert.Equal(namesake.Id, selectedNode.PersonId);
        await vm.AddBiologicalChildForTreeNodeAsync(selectedNode, "快捷子女");
        await using (var verify = database.CreateContext())
        {
            Assert.Equal(namesake.Id, (await verify.PersonRelationships.SingleAsync()).RelatedPersonId);
            Assert.Equal("同名", (await verify.People.SingleAsync(p => p.Id == firstPerson.Id)).Name);
        }
        vm.SelectedPerson = vm.People.Single(p => p.Id == namesake.Id);
        await vm.SaveTreePersonProfileAsync(vm.GetSelectedTreePersonNode(), "快捷编辑", PersonSex.Female, null, null, "档案");
        Assert.Contains(vm.People, p => p.Id == namesake.Id && p.Name == "快捷编辑");
        vm.SelectedPerson = vm.People.Single(p => p.Id == firstPerson.Id);
        var foreign = vm.SelectedPerson;
        vm.NewName = "乙谱";
        await vm.CreateAsync();
        Assert.Throws<InvalidOperationException>(() => vm.GetSelectedTreePersonNode());
        vm.SelectedPerson = foreign;
        Assert.Throws<InvalidOperationException>(() => vm.GetSelectedTreePersonNode());
        vm.ClearSelectedPerson();
        Assert.Throws<InvalidOperationException>(() => vm.GetSelectedTreePersonNode());
        await using var final = database.CreateContext();
        Assert.Single(await final.PersonRelationships.ToListAsync());
        Assert.Equal("同名", (await final.People.SingleAsync(p => p.Id == firstPerson.Id)).Name);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Tree_profile_save_targets_clicked_person_and_rejects_stale_or_foreign_nodes()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        var first = vm.SelectedGenealogy!;
        Person firstPerson, namesake;
        await using (var context = database.CreateContext())
        {
            var service = new GenealogyDataService(context);
            firstPerson = await service.AddPersonAsync(new Person { GenealogyId = first.Id, Name = "同名", Biography = "甲档案" });
            namesake = await service.AddPersonAsync(new Person { GenealogyId = first.Id, Name = "同名", Biography = "乙档案", RecordedGeneration = "谱载七世" });
        }
        await vm.SelectAsync(first);
        vm.SelectedPerson = vm.People.Single(p => p.Id == firstPerson.Id);
        var clicked = vm.TargetTree.Single(n => n.PersonId == namesake.Id);
        Assert.Equal(namesake.Id, vm.GetTreePerson(clicked).Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.SaveTreePersonProfileAsync(clicked, "  ", PersonSex.Female, "出生", null, "不保存"));
        await vm.SaveTreePersonProfileAsync(clicked, " 新姓名 ", PersonSex.Female, "出生", "逝世", "新生平");
        await using (var verify = database.CreateContext())
        {
            var unchanged = await verify.People.SingleAsync(p => p.Id == firstPerson.Id);
            var saved = await verify.People.SingleAsync(p => p.Id == namesake.Id);
            Assert.Equal("同名", unchanged.Name);
            Assert.Equal("甲档案", unchanged.Biography);
            Assert.Equal("新姓名", saved.Name);
            Assert.Equal(PersonSex.Female, saved.Sex);
            Assert.Equal("出生", saved.BirthDateText);
            Assert.Equal("逝世", saved.DeathDateText);
            Assert.Equal("新生平", saved.Biography);
            Assert.Equal("谱载七世", saved.RecordedGeneration);
        }
        Assert.Contains(vm.TargetTree, n => n.PersonId == namesake.Id && n.Label.Contains("新姓名"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.SaveTreePersonProfileAsync(clicked, "旧节点", PersonSex.Unknown, null, null, null));
        var currentNode = vm.TargetTree.Single(n => n.PersonId == namesake.Id);
        vm.NewName = "乙谱";
        await vm.CreateAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.SaveTreePersonProfileAsync(currentNode, "跨谱", PersonSex.Unknown, null, null, null));
        await using var foreignContext = database.CreateContext();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new GenealogyDataService(foreignContext)
            .UpdatePersonBasicProfileAsync(vm.SelectedGenealogy!.Id, namesake.Id, "跨谱", PersonSex.Unknown, null, null, null));
        Assert.Equal("新姓名", (await foreignContext.People.SingleAsync(p => p.Id == namesake.Id)).Name);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Tree_profile_save_invalid_or_database_failure_keeps_all_original_fields()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "失败谱" };
        await vm.CreateAsync();
        vm.NewPersonName = "原姓名";
        await vm.AddPersonAsync();
        var node = Assert.Single(vm.TargetTree);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.SaveTreePersonProfileAsync(node, "新姓名", PersonSex.Female, new string('甲', 101), null, "新生平"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.SaveTreePersonProfileAsync(node, "新姓名", (PersonSex)999, null, null, null));
        await using (var context = database.CreateContext())
            await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectProfile BEFORE UPDATE ON People BEGIN SELECT RAISE(ABORT, 'simulated failure'); END;");
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => vm.SaveTreePersonProfileAsync(node, "新姓名", PersonSex.Female, "新出生", null, "新生平"));
        await using var verify = database.CreateContext();
        var person = await verify.People.SingleAsync();
        Assert.Equal("原姓名", person.Name);
        Assert.Equal(PersonSex.Unknown, person.Sex);
        Assert.Null(person.BirthDateText);
        Assert.Null(person.Biography);
        Assert.Equal("原姓名", Assert.Single(vm.People).Name);
        Assert.Contains(vm.TargetTree, item => item.PersonId == person.Id && item.Label.Contains("原姓名"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Tree_context_add_targets_right_clicked_id_and_rejects_stale_or_foreign_nodes()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        var first = vm.SelectedGenealogy!;
        Person left, right;
        await using (var context = database.CreateContext())
        {
            var service = new GenealogyDataService(context);
            left = await service.AddPersonAsync(new Person { GenealogyId = first.Id, Name = "同名" });
            right = await service.AddPersonAsync(new Person { GenealogyId = first.Id, Name = "同名" });
        }
        await vm.SelectAsync(first);
        vm.SelectedPerson = vm.People.Single(p => p.Id == left.Id);
        var clicked = vm.TargetTree.Single(n => n.PersonId == right.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddBiologicalChildForTreeNodeAsync(clicked, "  "));
        await vm.AddBiologicalChildForTreeNodeAsync(clicked, "目标子女");
        await using (var verify = database.CreateContext())
        {
            var relation = await verify.PersonRelationships.SingleAsync();
            Assert.Equal(right.Id, relation.RelatedPersonId);
            Assert.Equal("目标子女", (await verify.People.SingleAsync(p => p.Id == relation.PersonId)).Name);
            Assert.Equal(first.Id, relation.GenealogyId);
        }
        Assert.Contains(vm.TargetTree, n => n.Label.Contains("目标子女"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddBiologicalChildForTreeNodeAsync(clicked, "过期子女"));
        var currentNode = vm.TargetTree.Single(n => n.PersonId == right.Id);
        vm.NewName = "乙谱";
        await vm.CreateAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddBiologicalChildForTreeNodeAsync(currentNode, "跨谱子女"));
        await using var final = database.CreateContext();
        Assert.Equal(3, await final.People.CountAsync());
        Assert.Single(await final.PersonRelationships.ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Tree_context_add_rolls_back_person_if_relationship_insert_fails()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "失败谱" };
        await vm.CreateAsync();
        vm.NewPersonName = "父母";
        await vm.AddPersonAsync();
        var node = Assert.Single(vm.TargetTree);
        await using (var context = database.CreateContext())
            await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectTreeChild BEFORE INSERT ON PersonRelationships BEGIN SELECT RAISE(ABORT, 'simulated failure'); END;");
        await Assert.ThrowsAsync<DbUpdateException>(() => vm.AddBiologicalChildForTreeNodeAsync(node, "子女"));
        await using var verify = database.CreateContext();
        Assert.Single(await verify.People.ToListAsync());
        Assert.Empty(await verify.PersonRelationships.ToListAsync());
        Assert.Equal("父母", Assert.Single(vm.People).Name);
        Assert.Single(vm.TargetTree);
        Assert.DoesNotContain(vm.TargetTree, item => item.Label.Contains("子女"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Tree_selection_uses_person_id_and_clears_on_switch_or_cancel()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        var first = vm.SelectedGenealogy!;
        vm.NewName = "乙谱";
        await vm.CreateAsync();
        var second = vm.SelectedGenealogy!;
        Person firstPerson, namesake, child, foreign;
        await using (var context = database.CreateContext())
        {
            var service = new GenealogyDataService(context);
            firstPerson = await service.AddPersonAsync(new Person { GenealogyId = first.Id, Name = "同名", Biography = "甲档案" });
            namesake = await service.AddPersonAsync(new Person { GenealogyId = first.Id, Name = "同名", Biography = "乙档案" });
            child = await service.AddPersonAsync(new Person { GenealogyId = first.Id, Name = "子女" });
            foreign = await service.AddPersonAsync(new Person { GenealogyId = second.Id, Name = "同名", Biography = "外谱" });
            await service.AddExistingBiologicalChildAsync(first.Id, firstPerson.Id, child.Id);
        }

        await vm.SelectAsync(first);
        var oldNode = vm.TargetTree.Single(n => n.PersonId == firstPerson.Id);
        await vm.SelectTreePersonAsync(oldNode);
        Assert.Equal(firstPerson.Id, vm.SelectedPerson!.Id);
        Assert.Equal("甲档案", vm.EditedBiography);
        Assert.Equal(child.Id, Assert.Single(vm.BiologicalChildren).Id);

        await vm.SelectTreePersonAsync(vm.TargetTree.Single(n => n.PersonId == namesake.Id));
        Assert.Equal(namesake.Id, vm.SelectedPerson!.Id);
        Assert.Equal("乙档案", vm.EditedBiography);
        Assert.Empty(vm.BiologicalChildren);

        vm.ClearSelectedPerson();
        Assert.Null(vm.SelectedPerson);
        Assert.Null(vm.EditedBiography);
        Assert.Empty(vm.BiologicalChildren);
        await vm.SelectTreePersonAsync(oldNode);
        Assert.Equal(firstPerson.Id, vm.SelectedPerson!.Id);

        await vm.SelectAsync(second);
        Assert.Null(vm.SelectedPerson);
        Assert.Empty(vm.BiologicalChildren);
        await vm.SelectTreePersonAsync(oldNode);
        Assert.Null(vm.SelectedPerson);
        await vm.SelectTreePersonAsync(Assert.Single(vm.TargetTree));
        Assert.Equal(foreign.Id, vm.SelectedPerson!.Id);
        Assert.Equal("外谱", vm.EditedBiography);
        await vm.SelectAsync(null);
        Assert.Null(vm.SelectedPerson);
        Assert.Null(vm.EditedBiography);
        Assert.Empty(vm.BiologicalChildren);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Create_and_switch_show_only_people_in_selected_genealogy()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext);
        vm.NewName = "  甲谱  ";
        await vm.CreateAsync();
        var first = vm.SelectedGenealogy!;
        Assert.Equal("甲谱", first.Name);

        vm.NewName = "乙谱";
        await vm.CreateAsync();
        var second = vm.SelectedGenealogy!;
        await using (var context = database.CreateContext())
        {
            var service = new GenealogyDataService(context);
            await service.AddPersonAsync(new Person { GenealogyId = first.Id, Name = "甲人物" });
            await service.AddPersonAsync(new Person { GenealogyId = second.Id, Name = "乙人物" });
        }

        await vm.SelectAsync(first);
        Assert.Equal("甲人物", Assert.Single(vm.People).Name);
        await vm.SelectAsync(second);
        Assert.Equal("乙人物", Assert.Single(vm.People).Name);
        await vm.SelectAsync(null);
        Assert.Empty(vm.People);

        var newSession = new GenealogySelectionViewModel(database.CreateContext);
        await newSession.LoadAsync();
        Assert.Equal(2, newSession.Genealogies.Count);
        Assert.Null(newSession.SelectedGenealogy);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Invalid_or_duplicate_name_keeps_existing_selection()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        var selectedId = vm.SelectedGenealogy!.Id;

        vm.NewName = "  ";
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.CreateAsync());
        vm.NewName = "甲谱";
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.CreateAsync());
        Assert.Equal(selectedId, vm.SelectedGenealogy!.Id);
        Assert.Single(vm.Genealogies);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Adding_people_uses_selected_genealogy_and_switching_keeps_them_separate()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        var first = vm.SelectedGenealogy!;
        vm.NewPersonName = "  虚构甲  ";
        await vm.AddPersonAsync();
        Assert.Equal("虚构甲", Assert.Single(vm.People).Name);

        vm.NewName = "乙谱";
        await vm.CreateAsync();
        var second = vm.SelectedGenealogy!;
        Assert.Empty(vm.People);
        vm.NewPersonName = "虚构乙";
        await vm.AddPersonAsync();
        Assert.Equal("虚构乙", Assert.Single(vm.People).Name);
        await vm.SelectAsync(first);
        Assert.Equal(first.Id, Assert.Single(vm.People).GenealogyId);
        Assert.Equal("虚构甲", vm.People[0].Name);
        await vm.SelectAsync(second);
        Assert.Equal("虚构乙", Assert.Single(vm.People).Name);
        await using var verify = database.CreateContext();
        Assert.Equal(2, await verify.People.CountAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Person_creation_rejects_missing_selection_invalid_name_and_deleted_genealogy()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewPersonName = "虚构人物" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddPersonAsync());
        vm.NewName = "甲谱";
        await vm.CreateAsync();
        vm.NewPersonName = "   ";
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddPersonAsync());
        vm.NewPersonName = new string('甲', 201);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddPersonAsync());
        vm.NewPersonName = "虚构人物";
        await using (var context = database.CreateContext())
        {
            var genealogy = await context.Genealogies.SingleAsync();
            context.Genealogies.Remove(genealogy);
            await context.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddPersonAsync());
        await using var verify = database.CreateContext();
        Assert.Empty(await verify.People.ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Rename_updates_only_selected_person_and_preserves_other_fields()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        var first = vm.SelectedGenealogy!;
        await using (var context = database.CreateContext())
            await new GenealogyDataService(context).AddPersonAsync(new Person
            {
                GenealogyId = first.Id, Name = "原名", Biography = "保留资料", RecordedGeneration = "谱载七世"
            });
        await vm.SelectAsync(first);
        vm.SelectedPerson = Assert.Single(vm.People);
        Assert.Equal("原名", vm.EditedPersonName);
        vm.EditedPersonName = "  新名  ";
        await vm.RenamePersonAsync();
        Assert.Equal("新名", Assert.Single(vm.People).Name);
        await using var verification = database.CreateContext();
        var saved = await verification.People.SingleAsync();
        Assert.Equal("保留资料", saved.Biography);
        Assert.Equal("谱载七世", saved.RecordedGeneration);
        Assert.Equal(first.Id, saved.GenealogyId);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Rename_rejects_cross_genealogy_invalid_or_missing_person()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        var first = vm.SelectedGenealogy!;
        vm.NewPersonName = "甲人物";
        await vm.AddPersonAsync();
        var foreign = Assert.Single(vm.People);
        vm.NewName = "乙谱";
        await vm.CreateAsync();
        vm.NewPersonName = "乙人物";
        await vm.AddPersonAsync();
        var secondPerson = Assert.Single(vm.People);

        vm.SelectedPerson = foreign;
        vm.EditedPersonName = "不能跨谱修改";
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RenamePersonAsync());
        vm.SelectedPerson = secondPerson;
        vm.EditedPersonName = "   ";
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RenamePersonAsync());
        vm.EditedPersonName = new string('甲', 201);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RenamePersonAsync());
        vm.SelectedPerson = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RenamePersonAsync());
        await vm.SelectAsync(first);
        Assert.Equal("甲人物", Assert.Single(vm.People).Name);
        await using var verification = database.CreateContext();
        Assert.Equal(new[] { "乙人物", "甲人物" }, await verification.People.OrderBy(p => p.Name).Select(p => p.Name).ToArrayAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Add_child_creates_child_and_biological_parent_relationship_in_current_genealogy()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        var genealogy = vm.SelectedGenealogy!;
        vm.NewPersonName = "虚构父母";
        await vm.AddPersonAsync();
        var parent = Assert.Single(vm.People);
        vm.SelectedPerson = parent;
        vm.NewChildName = "  虚构子女  ";
        await vm.AddBiologicalChildAsync();
        Assert.Contains(vm.People, p => p.Name == "虚构子女" && p.GenealogyId == genealogy.Id);
        Assert.Equal(string.Empty, vm.NewChildName);
        await using var verify = database.CreateContext();
        var child = await verify.People.SingleAsync(p => p.Name == "虚构子女");
        var relation = await verify.PersonRelationships.SingleAsync();
        Assert.Equal(genealogy.Id, relation.GenealogyId);
        Assert.Equal(child.Id, relation.PersonId);
        Assert.Equal(parent.Id, relation.RelatedPersonId);
        Assert.Equal(PersonRelationshipKind.BiologicalParent, relation.Kind);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Add_child_rejects_invalid_input_and_foreign_or_missing_parent_without_writing_child()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewChildName = "子女" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddBiologicalChildAsync());
        vm.NewName = "甲谱";
        await vm.CreateAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddBiologicalChildAsync());
        vm.NewPersonName = "甲父母";
        await vm.AddPersonAsync();
        var foreignParent = Assert.Single(vm.People);
        vm.SelectedPerson = foreignParent;
        vm.NewChildName = "   ";
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddBiologicalChildAsync());
        vm.NewChildName = new string('子', 201);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddBiologicalChildAsync());
        vm.NewName = "乙谱";
        await vm.CreateAsync();
        vm.SelectedPerson = foreignParent;
        vm.NewChildName = "跨谱子女";
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddBiologicalChildAsync());
        Assert.Contains("跨族谱", error.Message);
        vm.SelectedPerson = new Person { Id = Guid.NewGuid(), GenealogyId = vm.SelectedGenealogy!.Id, Name = "不存在" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddBiologicalChildAsync());
        await using var verify = database.CreateContext();
        Assert.Single(await verify.People.ToListAsync());
        Assert.Empty(await verify.PersonRelationships.ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Relationship_insert_failure_rolls_back_new_child()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var service = new GenealogyDataService(context);
        var genealogy = await service.CreateGenealogyAsync(new Genealogy { Name = "写入失败谱" });
        var parent = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "虚构父母" });
        await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectRelationships BEFORE INSERT ON PersonRelationships BEGIN SELECT RAISE(ABORT, 'simulated failure'); END;");
        await Assert.ThrowsAsync<DbUpdateException>(() => service.AddBiologicalChildAsync(genealogy.Id, parent.Id, "虚构子女"));
        await using var verify = database.CreateContext();
        Assert.Equal(parent.Id, (await verify.People.SingleAsync()).Id);
        Assert.Empty(await verify.PersonRelationships.ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Clearing_parent_selection_reports_no_parent_and_rejects_child_creation()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "选择测试谱" };
        await vm.CreateAsync();
        vm.NewPersonName = "虚构父母";
        await vm.AddPersonAsync();
        vm.SelectedPerson = Assert.Single(vm.People);
        Assert.Contains("添加子女的父母：虚构父母", vm.SelectedParentStatus);
        Assert.Contains(vm.SelectedPerson!.Id.ToString(), vm.SelectedParentStatus);
        vm.ClearSelectedPerson();
        Assert.Null(vm.SelectedPerson);
        Assert.Equal("添加子女的父母：未选择", vm.SelectedParentStatus);
        vm.NewChildName = "虚构子女";
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddBiologicalChildAsync());
        Assert.Equal("请先选择父母人物。", exception.Message);
        await using var verification = database.CreateContext();
        Assert.Single(await verification.People.ToListAsync());
        Assert.Empty(await verification.PersonRelationships.ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Selected_person_shows_only_their_same_genealogy_biological_children()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        var first = vm.SelectedGenealogy!;
        vm.NewPersonName = "甲父母";
        await vm.AddPersonAsync();
        var parent = Assert.Single(vm.People);
        vm.SelectedPerson = parent;
        vm.NewChildName = "生物子女";
        await vm.AddBiologicalChildAsync();
        await using (var context = database.CreateContext())
        {
            var service = new GenealogyDataService(context);
            var adopted = await service.AddPersonAsync(new Person { GenealogyId = first.Id, Name = "收养子女" });
            await service.AddRelationshipAsync(new PersonRelationship { GenealogyId = first.Id, PersonId = adopted.Id, RelatedPersonId = parent.Id, Kind = PersonRelationshipKind.AdoptiveParent });
        }
        await vm.SelectAsync(first);
        vm.SelectedPerson = vm.People.Single(p => p.Id == parent.Id);
        await vm.LoadSelectedBiologicalChildrenAsync();
        Assert.Equal("生物子女", Assert.Single(vm.BiologicalChildren).Name);
        vm.SelectedPerson = vm.People.Single(p => p.Name == "收养子女");
        Assert.Empty(vm.BiologicalChildren);
        await vm.LoadSelectedBiologicalChildrenAsync();
        Assert.Empty(vm.BiologicalChildren);
        Assert.Contains("0 人", vm.ChildrenStatus);

        vm.NewName = "乙谱";
        await vm.CreateAsync();
        vm.NewPersonName = "乙父母";
        await vm.AddPersonAsync();
        vm.SelectedPerson = Assert.Single(vm.People);
        vm.NewChildName = "乙子女";
        await vm.AddBiologicalChildAsync();
        await vm.SelectAsync(first);
        vm.SelectedPerson = vm.People.Single(p => p.Id == parent.Id);
        await vm.LoadSelectedBiologicalChildrenAsync();
        Assert.Equal("生物子女", Assert.Single(vm.BiologicalChildren).Name);
        vm.ClearSelectedPerson();
        Assert.Empty(vm.BiologicalChildren);
        Assert.Equal("请选择人物查看生物子女", vm.ChildrenStatus);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Biological_children_query_is_scoped_and_does_not_return_foreign_parent_children()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var service = new GenealogyDataService(context);
        var first = await service.CreateGenealogyAsync(new Genealogy { Name = "甲谱" });
        var second = await service.CreateGenealogyAsync(new Genealogy { Name = "乙谱" });
        var parent = await service.AddPersonAsync(new Person { GenealogyId = first.Id, Name = "甲父母" });
        await service.AddBiologicalChildAsync(first.Id, parent.Id, "甲子女");
        Assert.Single(await service.GetBiologicalChildrenAsync(first.Id, parent.Id));
        Assert.Empty(await service.GetBiologicalChildrenAsync(second.Id, parent.Id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Profile_edit_round_trips_text_and_preserves_genealogy_and_recorded_values()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        var genealogy = vm.SelectedGenealogy!;
        await using (var context = database.CreateContext())
            await new GenealogyDataService(context).AddPersonAsync(new Person
            {
                GenealogyId = genealogy.Id, Name = "虚构人物", RecordedGeneration = "谱载七世", DerivedGeneration = 8,
                RecordedBirthOrder = "长", DerivedBirthOrder = 1, PhotoPath = "保留照片"
            });
        await vm.SelectAsync(genealogy);
        vm.SelectedPerson = Assert.Single(vm.People);
        Assert.Equal(PersonSex.Unknown, vm.EditedSex);
        vm.EditedSex = PersonSex.Female;
        vm.EditedBirthDate = "清末某年";
        vm.EditedDeathDate = "失考";
        vm.EditedBiography = "虚构生平";
        await vm.SavePersonProfileAsync();
        await using var verify = database.CreateContext();
        var saved = await verify.People.SingleAsync();
        Assert.Equal(genealogy.Id, saved.GenealogyId);
        Assert.Equal("虚构人物", saved.Name);
        Assert.Equal(PersonSex.Female, saved.Sex);
        Assert.Equal("清末某年", saved.BirthDateText);
        Assert.Equal("失考", saved.DeathDateText);
        Assert.Equal("虚构生平", saved.Biography);
        Assert.Equal("谱载七世", saved.RecordedGeneration);
        Assert.Equal(8, saved.DerivedGeneration);
        Assert.Equal("长", saved.RecordedBirthOrder);
        Assert.Equal(1, saved.DerivedBirthOrder);
        Assert.Equal("保留照片", saved.PhotoPath);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Profile_edit_rejects_foreign_selection_and_invalid_values_without_modification()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        vm.NewPersonName = "甲人物";
        await vm.AddPersonAsync();
        var foreign = Assert.Single(vm.People);
        vm.NewName = "乙谱";
        await vm.CreateAsync();
        vm.NewPersonName = "乙人物";
        await vm.AddPersonAsync();
        var current = Assert.Single(vm.People);
        vm.SelectedPerson = foreign;
        vm.EditedBiography = "不应写入";
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.SavePersonProfileAsync());
        vm.SelectedPerson = current;
        vm.EditedBirthDate = new string('甲', 101);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.SavePersonProfileAsync());
        vm.EditedBirthDate = null;
        vm.EditedBiography = new string('甲', 4001);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.SavePersonProfileAsync());
        vm.EditedBiography = null;
        vm.EditedSex = (PersonSex)999;
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.SavePersonProfileAsync());
        await using var verify = database.CreateContext();
        Assert.All(await verify.People.ToListAsync(), p => { Assert.Null(p.Biography); Assert.Null(p.BirthDateText); Assert.Equal(PersonSex.Unknown, p.Sex); });
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Delete_unrelated_person_is_scoped_to_selected_genealogy()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        vm.NewPersonName = "甲人物";
        await vm.AddPersonAsync();
        var foreign = Assert.Single(vm.People);
        vm.NewName = "乙谱";
        await vm.CreateAsync();
        vm.NewPersonName = "乙人物";
        await vm.AddPersonAsync();
        var current = Assert.Single(vm.People);
        vm.SelectedPerson = foreign;
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.DeleteUnrelatedPersonAsync());
        vm.SelectedPerson = current;
        await vm.DeleteUnrelatedPersonAsync();
        Assert.Empty(vm.People);
        Assert.Null(vm.SelectedPerson);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.DeleteUnrelatedPersonAsync());
        await using var verify = database.CreateContext();
        Assert.Equal(foreign.Id, (await verify.People.SingleAsync()).Id);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Delete_rejects_relationship_subject_related_person_and_lost_ancestry()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var service = new GenealogyDataService(context);
        var genealogy = await service.CreateGenealogyAsync(new Genealogy { Name = "关系保护谱" });
        var parent = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "父母" });
        var child = await service.AddBiologicalChildAsync(genealogy.Id, parent.Id, "子女");
        var lost = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "失考人物" });
        await service.AddRelationshipAsync(new PersonRelationship { GenealogyId = genealogy.Id, PersonId = lost.Id, Kind = PersonRelationshipKind.LostAncestry });
        foreach (var person in new[] { parent, child, lost })
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteUnrelatedPersonAsync(genealogy.Id, person.Id));
        await using var verify = database.CreateContext();
        Assert.Equal(3, await verify.People.CountAsync());
        Assert.Equal(2, await verify.PersonRelationships.CountAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Delete_rejects_either_partner_of_a_family_union()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var service = new GenealogyDataService(context);
        var genealogy = await service.CreateGenealogyAsync(new Genealogy { Name = "婚配保护谱" });
        var first = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "甲" });
        var second = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "乙" });
        context.FamilyUnions.Add(new FamilyUnion { GenealogyId = genealogy.Id, PartnerOneId = first.Id, PartnerTwoId = second.Id });
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteUnrelatedPersonAsync(genealogy.Id, first.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteUnrelatedPersonAsync(genealogy.Id, second.Id));
        await using var verify = database.CreateContext();
        Assert.Equal(2, await verify.People.CountAsync());
        Assert.Single(await verify.FamilyUnions.ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Remove_biological_parent_relation_preserves_people_and_other_relationships()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "关系谱" };
        await vm.CreateAsync();
        var genealogy = vm.SelectedGenealogy!;
        vm.NewPersonName = "父母";
        await vm.AddPersonAsync();
        var parent = Assert.Single(vm.People);
        vm.SelectedPerson = parent;
        vm.NewChildName = "子女";
        await vm.AddBiologicalChildAsync();
        var child = vm.People.Single(p => p.Name == "子女");
        await using (var context = database.CreateContext())
        {
            await new GenealogyDataService(context).AddBiologicalChildAsync(genealogy.Id, parent.Id, "另一子女");
            await new GenealogyDataService(context).AddRelationshipAsync(new PersonRelationship
            {
                GenealogyId = genealogy.Id, PersonId = child.Id, RelatedPersonId = parent.Id, Kind = PersonRelationshipKind.AdoptiveParent
            });
        }
        vm.SelectedPerson = vm.People.Single(p => p.Id == parent.Id);
        await vm.LoadSelectedBiologicalChildrenAsync();
        vm.SelectedBiologicalChild = vm.BiologicalChildren.Single(p => p.Id == child.Id);
        await vm.RemoveBiologicalParentRelationshipAsync();
        Assert.Equal("另一子女", Assert.Single(vm.BiologicalChildren).Name);
        Assert.Null(vm.SelectedBiologicalChild);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RemoveBiologicalParentRelationshipAsync());
        await using var verify = database.CreateContext();
        Assert.Equal(3, await verify.People.CountAsync());
        Assert.Equal(2, await verify.PersonRelationships.CountAsync());
        Assert.Equal(PersonRelationshipKind.AdoptiveParent, (await verify.PersonRelationships.SingleAsync(r => r.PersonId == child.Id)).Kind);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new GenealogyDataService(verify).RemoveBiologicalParentRelationshipAsync(genealogy.Id, parent.Id, child.Id));
        Assert.Equal(PersonRelationshipKind.AdoptiveParent, (await verify.PersonRelationships.SingleAsync(r => r.PersonId == child.Id)).Kind);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Remove_relation_rejects_foreign_missing_or_wrong_kind_without_change()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var service = new GenealogyDataService(context);
        var first = await service.CreateGenealogyAsync(new Genealogy { Name = "甲谱" });
        var second = await service.CreateGenealogyAsync(new Genealogy { Name = "乙谱" });
        var parent = await service.AddPersonAsync(new Person { GenealogyId = first.Id, Name = "父母" });
        var child = await service.AddBiologicalChildAsync(first.Id, parent.Id, "子女");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RemoveBiologicalParentRelationshipAsync(second.Id, parent.Id, child.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RemoveBiologicalParentRelationshipAsync(first.Id, child.Id, parent.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RemoveBiologicalParentRelationshipAsync(first.Id, parent.Id, Guid.NewGuid()));
        await using var verify = database.CreateContext();
        Assert.Equal(2, await verify.People.CountAsync());
        Assert.Single(await verify.PersonRelationships.ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Failed_relation_removal_leaves_people_and_relation_intact()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var service = new GenealogyDataService(context);
        var genealogy = await service.CreateGenealogyAsync(new Genealogy { Name = "失败谱" });
        var parent = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "父母" });
        var child = await service.AddBiologicalChildAsync(genealogy.Id, parent.Id, "子女");
        await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectRelationshipDelete BEFORE DELETE ON PersonRelationships BEGIN SELECT RAISE(ABORT, 'simulated failure'); END;");
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => service.RemoveBiologicalParentRelationshipAsync(genealogy.Id, parent.Id, child.Id));
        await using var verify = database.CreateContext();
        Assert.Equal(2, await verify.People.CountAsync());
        Assert.Single(await verify.PersonRelationships.ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Existing_people_can_be_linked_and_relinked_without_recreating_records()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "关联谱" };
        await vm.CreateAsync();
        var genealogy = vm.SelectedGenealogy!;
        vm.NewPersonName = "父母";
        await vm.AddPersonAsync();
        vm.NewPersonName = "已有子女";
        await vm.AddPersonAsync();
        var parent = vm.People.Single(p => p.Name == "父母");
        var child = vm.People.Single(p => p.Name == "已有子女");
        vm.SelectedPerson = parent;
        vm.SelectedExistingChild = child;
        await vm.AddExistingBiologicalChildAsync();
        Assert.Equal(child.Id, Assert.Single(vm.BiologicalChildren).Id);
        Assert.Contains(vm.TargetTree.Single(n => n.PersonId == parent.Id).Details, d => d.Contains(child.Id.ToString()));
        vm.SelectedExistingChild = child;
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddExistingBiologicalChildAsync());
        vm.SelectedBiologicalChild = Assert.Single(vm.BiologicalChildren);
        await vm.RemoveBiologicalParentRelationshipAsync();
        Assert.DoesNotContain(vm.TargetTree.Single(n => n.PersonId == parent.Id).Details, d => d.Contains(child.Id.ToString()));
        vm.SelectedExistingChild = child;
        await vm.AddExistingBiologicalChildAsync();
        await using var verify = database.CreateContext();
        Assert.Equal(2, await verify.People.CountAsync());
        var relation = await verify.PersonRelationships.SingleAsync();
        Assert.Equal(genealogy.Id, relation.GenealogyId);
        Assert.Equal(child.Id, relation.PersonId);
        Assert.Equal(parent.Id, relation.RelatedPersonId);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Existing_child_link_rejects_cross_genealogy_self_and_cycle()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var service = new GenealogyDataService(context);
        var first = await service.CreateGenealogyAsync(new Genealogy { Name = "甲谱" });
        var second = await service.CreateGenealogyAsync(new Genealogy { Name = "乙谱" });
        var parent = await service.AddPersonAsync(new Person { GenealogyId = first.Id, Name = "父母" });
        var child = await service.AddPersonAsync(new Person { GenealogyId = first.Id, Name = "子女" });
        var foreign = await service.AddPersonAsync(new Person { GenealogyId = second.Id, Name = "外谱" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddExistingBiologicalChildAsync(first.Id, parent.Id, foreign.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddExistingBiologicalChildAsync(first.Id, foreign.Id, child.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddExistingBiologicalChildAsync(first.Id, parent.Id, parent.Id));
        await service.AddExistingBiologicalChildAsync(first.Id, parent.Id, child.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddExistingBiologicalChildAsync(first.Id, child.Id, parent.Id));
        await using var verify = database.CreateContext();
        Assert.Equal(3, await verify.People.CountAsync());
        Assert.Single(await verify.PersonRelationships.ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Existing_child_link_failure_keeps_people_and_writes_no_relation()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var service = new GenealogyDataService(context);
        var genealogy = await service.CreateGenealogyAsync(new Genealogy { Name = "关联失败谱" });
        var parent = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "父母" });
        var child = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "子女" });
        await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectExistingChild BEFORE INSERT ON PersonRelationships BEGIN SELECT RAISE(ABORT, 'simulated failure'); END;");
        await Assert.ThrowsAsync<DbUpdateException>(() => service.AddExistingBiologicalChildAsync(genealogy.Id, parent.Id, child.Id));
        await using var verify = database.CreateContext();
        Assert.Equal(2, await verify.People.CountAsync());
        Assert.Empty(await verify.PersonRelationships.ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Existing_child_link_and_unlink_failures_keep_the_current_view_and_graph_intact()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "关联视图失败谱" };
        await vm.CreateAsync();
        var genealogy = vm.SelectedGenealogy!;
        foreach (var name in new[] { "父母", "子女" })
        {
            vm.NewPersonName = name;
            await vm.AddPersonAsync();
        }

        var parent = vm.People.Single(person => person.Name == "父母");
        var child = vm.People.Single(person => person.Name == "子女");
        vm.SelectedPerson = parent;
        vm.SelectedExistingChild = child;
        await using (var failure = database.CreateContext())
            await failure.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectViewLink BEFORE INSERT ON PersonRelationships BEGIN SELECT RAISE(ABORT, 'simulated link failure'); END;");
        await Assert.ThrowsAsync<DbUpdateException>(() => vm.AddExistingBiologicalChildAsync());
        Assert.Equal(child.Id, vm.SelectedExistingChild!.Id);
        Assert.Empty(vm.BiologicalChildren);
        Assert.DoesNotContain(vm.TargetTree.Single(item => item.PersonId == parent.Id).Details, detail => detail.Contains(child.Id.ToString()));

        await using (var cleanup = database.CreateContext())
            await cleanup.Database.ExecuteSqlRawAsync("DROP TRIGGER RejectViewLink;");
        await vm.AddExistingBiologicalChildAsync();
        Assert.Equal(child.Id, Assert.Single(vm.BiologicalChildren).Id);
        vm.SelectedBiologicalChild = vm.BiologicalChildren.Single();
        await using (var failure = database.CreateContext())
            await failure.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectViewUnlink BEFORE DELETE ON PersonRelationships BEGIN SELECT RAISE(ABORT, 'simulated unlink failure'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => vm.RemoveBiologicalParentRelationshipAsync());
        Assert.Equal(child.Id, vm.SelectedBiologicalChild!.Id);
        Assert.Equal(child.Id, Assert.Single(vm.BiologicalChildren).Id);
        Assert.Contains(vm.TargetTree.Single(item => item.PersonId == parent.Id).Details, detail => detail.Contains(child.Id.ToString()));

        await using var verify = database.CreateContext();
        Assert.Equal(2, await verify.People.CountAsync(item => item.GenealogyId == genealogy.Id));
        Assert.Single(await verify.PersonRelationships.Where(item => item.GenealogyId == genealogy.Id).ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Selection_busy_retry_exhaustion_keeps_a_consistent_target_state_and_can_reload()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var options = new DbContextOptionsBuilder<ZupuDbContext>()
            .UseSqlite($"Data Source={database.DatabasePath};Pooling=False;Default Timeout=1").Options;
        var vm = new GenealogySelectionViewModel(() => new ZupuDbContext(options)) { NewName = "甲谱" };
        await vm.CreateAsync();
        var first = vm.SelectedGenealogy!;
        vm.NewPersonName = "甲人物";
        await vm.AddPersonAsync();
        vm.SelectedPerson = Assert.Single(vm.People);
        vm.NewName = "乙谱";
        await vm.CreateAsync();
        var second = vm.SelectedGenealogy!;

        await using var blocker = new SqliteConnection($"Data Source={database.DatabasePath};Pooling=False;Default Timeout=1");
        await blocker.OpenAsync();
        await using var command = blocker.CreateCommand();
        command.CommandText = "BEGIN EXCLUSIVE;";
        await command.ExecuteNonQueryAsync();
        try
        {
            var failure = await Assert.ThrowsAsync<SqliteException>(() => vm.SelectAsync(first));
            Assert.Equal(5, failure.SqliteErrorCode);
            Assert.Equal(first.Id, vm.SelectedGenealogy!.Id);
            Assert.Null(vm.SelectedPerson);
            Assert.Empty(vm.BiologicalChildren);
            Assert.Empty(vm.People);
            Assert.Empty(vm.TargetTree);
            Assert.StartsWith("加载目标谱系失败：", vm.Status);
        }
        finally
        {
            command.CommandText = "ROLLBACK;";
            await command.ExecuteNonQueryAsync();
        }

        await vm.SelectAsync(first);
        Assert.Equal(first.Id, vm.SelectedGenealogy!.Id);
        Assert.Equal("甲人物", Assert.Single(vm.People).Name);
        Assert.Single(vm.TargetTree);
        await using var verify = new ZupuDbContext(options);
        Assert.Equal(1, await verify.People.CountAsync(person => person.GenealogyId == first.Id));
        Assert.Equal(0, await verify.People.CountAsync(person => person.GenealogyId == second.Id));
        Assert.Empty(await verify.PersonRelationships.ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Committed_tree_and_biological_child_writes_report_refresh_failure_and_can_reload()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var refreshFailure = new FailRefreshAfterWriteInterceptor();
        var options = new DbContextOptionsBuilder<ZupuDbContext>()
            .UseSqlite($"Data Source={database.DatabasePath};Pooling=False")
            .AddInterceptors(refreshFailure).Options;
        var vm = new GenealogySelectionViewModel(() => new ZupuDbContext(options)) { NewName = "刷新失败谱" };
        await vm.CreateAsync();
        var genealogy = vm.SelectedGenealogy!;
        foreach (var name in new[] { "父母", "已有子女" })
        {
            vm.NewPersonName = name;
            await vm.AddPersonAsync();
        }

        var parentId = vm.People.Single(person => person.Name == "父母").Id;
        var existingChildId = vm.People.Single(person => person.Name == "已有子女").Id;

        refreshFailure.Arm("UPDATE \"People\"");
        await vm.SaveTreePersonProfileAsync(vm.TargetTree.Single(item => item.PersonId == parentId), "已保存父母", PersonSex.Male, null, null, "已保存档案");
        Assert.StartsWith("档案已保存，但目标谱系刷新失败：", vm.Status);
        Assert.Empty(vm.People);
        Assert.Empty(vm.TargetTree);
        await using (var verify = database.CreateContext())
            Assert.Equal("已保存档案", (await verify.People.SingleAsync(item => item.Id == parentId)).Biography);
        refreshFailure.Disable();
        await vm.SelectAsync(genealogy);
        Assert.Contains(vm.People, item => item.Id == parentId && item.Name == "已保存父母");

        refreshFailure.Arm("INSERT INTO \"PersonRelationships\"");
        await vm.AddBiologicalChildForTreeNodeAsync(vm.TargetTree.Single(item => item.PersonId == parentId), "新子女");
        Assert.StartsWith("子女已保存，但目标谱系刷新失败：", vm.Status);
        Assert.Empty(vm.People);
        Assert.Empty(vm.TargetTree);
        await using (var verify = database.CreateContext())
        {
            Assert.Equal(3, await verify.People.CountAsync(item => item.GenealogyId == genealogy.Id));
            Assert.Single(await verify.PersonRelationships.Where(item => item.GenealogyId == genealogy.Id).ToListAsync());
        }
        refreshFailure.Disable();
        await vm.SelectAsync(genealogy);

        vm.SelectedPerson = vm.People.Single(item => item.Id == parentId);
        vm.SelectedExistingChild = vm.People.Single(item => item.Id == existingChildId);
        refreshFailure.Arm("INSERT INTO \"PersonRelationships\"");
        await vm.AddExistingBiologicalChildAsync();
        Assert.StartsWith("生物父子关系已建立，但目标谱系刷新失败：", vm.Status);
        Assert.StartsWith("加载生物子女失败：", vm.ChildrenStatus);
        Assert.Empty(vm.TargetTree);
        await using (var verify = database.CreateContext())
            Assert.Equal(2, await verify.PersonRelationships.CountAsync(item => item.GenealogyId == genealogy.Id));
        refreshFailure.Disable();
        await vm.SelectAsync(genealogy);
        vm.SelectedPerson = vm.People.Single(item => item.Id == parentId);
        await vm.LoadSelectedBiologicalChildrenAsync();
        Assert.Equal(2, vm.BiologicalChildren.Count);

        vm.SelectedBiologicalChild = vm.BiologicalChildren.Single(item => item.Id == existingChildId);
        refreshFailure.Arm("DELETE FROM \"PersonRelationships\"");
        await vm.RemoveBiologicalParentRelationshipAsync();
        Assert.StartsWith("生物父子关系已解除，但目标谱系刷新失败：", vm.Status);
        Assert.StartsWith("加载生物子女失败：", vm.ChildrenStatus);
        Assert.Empty(vm.TargetTree);
        await using (var verify = database.CreateContext())
            Assert.Single(await verify.PersonRelationships.Where(item => item.GenealogyId == genealogy.Id).ToListAsync());
        refreshFailure.Disable();
        await vm.SelectAsync(genealogy);
        vm.SelectedPerson = vm.People.Single(item => item.Id == parentId);
        await vm.LoadSelectedBiologicalChildrenAsync();
        Assert.DoesNotContain(vm.BiologicalChildren, item => item.Id == existingChildId);
        Assert.Single(vm.BiologicalChildren);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Target_relationship_and_union_editing_is_scoped_to_selected_genealogy()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        var firstGenealogy = vm.SelectedGenealogy!;
        foreach (var name in new[] { "甲", "乙", "丙" })
        {
            vm.NewPersonName = name;
            await vm.AddPersonAsync();
        }
        var first = vm.People.Single(person => person.Name == "甲");
        var second = vm.People.Single(person => person.Name == "乙");
        var third = vm.People.Single(person => person.Name == "丙");
        vm.SelectedPerson = first;

        await vm.AddTargetRelationshipAsync(PersonRelationshipKind.Spouse, second.Id);
        await vm.AddTargetFamilyUnionAsync(FamilyUnionKind.Marriage, third.Id);
        Assert.Equal("已新增婚配关系。", vm.Status);

        vm.NewName = "乙谱";
        await vm.CreateAsync();
        var secondGenealogy = vm.SelectedGenealogy!;
        vm.NewPersonName = "外谱人物";
        await vm.AddPersonAsync();
        var foreign = Assert.Single(vm.People);
        await vm.SelectAsync(firstGenealogy);
        vm.SelectedPerson = vm.People.Single(person => person.Id == first.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddTargetRelationshipAsync(PersonRelationshipKind.Spouse, foreign.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddTargetFamilyUnionAsync(FamilyUnionKind.Marriage, foreign.Id));

        await using var verify = database.CreateContext();
        var relationship = await verify.PersonRelationships.SingleAsync();
        Assert.Equal(firstGenealogy.Id, relationship.GenealogyId);
        Assert.Equal(first.Id, relationship.PersonId);
        Assert.Equal(second.Id, relationship.RelatedPersonId);
        var union = await verify.FamilyUnions.SingleAsync();
        Assert.Equal(firstGenealogy.Id, union.GenealogyId);
        Assert.Contains(first.Id, new[] { union.PartnerOneId, union.PartnerTwoId!.Value });
        Assert.Contains(third.Id, new[] { union.PartnerOneId, union.PartnerTwoId!.Value });
        Assert.Equal(secondGenealogy.Id, foreign.GenealogyId);
        Assert.Equal(0, await verify.PersonRelationships.CountAsync(item => item.GenealogyId == secondGenealogy.Id));
        Assert.Equal(0, await verify.FamilyUnions.CountAsync(item => item.GenealogyId == secondGenealogy.Id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Target_relationship_editing_rejects_stale_or_foreign_selected_person_without_writing()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "甲谱" };
        await vm.CreateAsync();
        vm.NewPersonName = "甲人物";
        await vm.AddPersonAsync();
        var firstPerson = Assert.Single(vm.People);
        vm.NewName = "乙谱";
        await vm.CreateAsync();
        vm.NewPersonName = "乙人物";
        await vm.AddPersonAsync();
        var secondPerson = Assert.Single(vm.People);
        vm.SelectedPerson = firstPerson;

        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddTargetRelationshipAsync(PersonRelationshipKind.Spouse, secondPerson.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.AddTargetFamilyUnionAsync(FamilyUnionKind.Marriage, secondPerson.Id));
        await using var verify = database.CreateContext();
        Assert.Empty(await verify.PersonRelationships.ToListAsync());
        Assert.Empty(await verify.FamilyUnions.ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Target_link_removal_deletes_only_selected_scoped_record_and_keeps_people()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var vm = new GenealogySelectionViewModel(database.CreateContext) { NewName = "解除谱" };
        await vm.CreateAsync();
        var genealogy = vm.SelectedGenealogy!;
        foreach (var name in new[] { "甲", "乙", "丙" })
        {
            vm.NewPersonName = name;
            await vm.AddPersonAsync();
        }
        var first = vm.People.Single(person => person.Name == "甲");
        var second = vm.People.Single(person => person.Name == "乙");
        var third = vm.People.Single(person => person.Name == "丙");
        vm.SelectedPerson = first;
        await vm.AddTargetRelationshipAsync(PersonRelationshipKind.Spouse, second.Id);
        await vm.AddTargetFamilyUnionAsync(FamilyUnionKind.Marriage, third.Id);

        var candidates = await vm.GetTargetLinkRemovalCandidatesAsync();
        var relationship = Assert.Single(candidates, item => item.Kind == TargetLinkRemovalKind.Relationship);
        var union = Assert.Single(candidates, item => item.Kind == TargetLinkRemovalKind.FamilyUnion);
        await vm.RemoveTargetLinkAsync(relationship);
        Assert.Equal("已解除人物关系。", vm.Status);
        await using (var afterRelationship = database.CreateContext())
        {
            Assert.Equal(3, await afterRelationship.People.CountAsync());
            Assert.Empty(await afterRelationship.PersonRelationships.ToListAsync());
            Assert.Single(await afterRelationship.FamilyUnions.ToListAsync());
        }

        await vm.RemoveTargetLinkAsync(union);
        Assert.Equal("已解除婚配关系。", vm.Status);
        await using var verify = database.CreateContext();
        Assert.Equal(3, await verify.People.CountAsync());
        Assert.Empty(await verify.PersonRelationships.ToListAsync());
        Assert.Empty(await verify.FamilyUnions.ToListAsync());
        Assert.Equal(genealogy.Id, first.GenealogyId);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Target_link_removal_rejects_foreign_or_stale_record_without_deleting_anything()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var service = new GenealogyDataService(context);
        var firstGenealogy = await service.CreateGenealogyAsync(new Genealogy { Name = "甲谱" });
        var secondGenealogy = await service.CreateGenealogyAsync(new Genealogy { Name = "乙谱" });
        var first = await service.AddPersonAsync(new Person { GenealogyId = firstGenealogy.Id, Name = "甲" });
        var firstOther = await service.AddPersonAsync(new Person { GenealogyId = firstGenealogy.Id, Name = "乙" });
        var foreign = await service.AddPersonAsync(new Person { GenealogyId = secondGenealogy.Id, Name = "外甲" });
        var foreignOther = await service.AddPersonAsync(new Person { GenealogyId = secondGenealogy.Id, Name = "外乙" });
        var ownRelationship = await service.AddRelationshipAsync(new PersonRelationship
        {
            GenealogyId = firstGenealogy.Id, PersonId = first.Id, RelatedPersonId = firstOther.Id, Kind = PersonRelationshipKind.Spouse
        });
        var foreignUnion = await service.AddFamilyUnionAsync(new FamilyUnion
        {
            GenealogyId = secondGenealogy.Id, PartnerOneId = foreign.Id, PartnerTwoId = foreignOther.Id
        });

        var vm = new GenealogySelectionViewModel(database.CreateContext);
        await vm.LoadAsync();
        await vm.SelectAsync(vm.Genealogies.Single(item => item.Id == firstGenealogy.Id));
        vm.SelectedPerson = vm.People.Single(item => item.Id == first.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RemoveTargetLinkAsync(
            new TargetLinkRemovalCandidate(foreignUnion.Id, TargetLinkRemovalKind.FamilyUnion, "伪造跨谱婚配")));
        await vm.RemoveTargetLinkAsync(new TargetLinkRemovalCandidate(ownRelationship.Id, TargetLinkRemovalKind.Relationship, "当前关系"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RemoveTargetLinkAsync(
            new TargetLinkRemovalCandidate(ownRelationship.Id, TargetLinkRemovalKind.Relationship, "已解除关系")));

        await using var verify = database.CreateContext();
        Assert.Empty(await verify.PersonRelationships.Where(item => item.GenealogyId == firstGenealogy.Id).ToListAsync());
        Assert.Single(await verify.FamilyUnions.Where(item => item.GenealogyId == secondGenealogy.Id).ToListAsync());
        Assert.Equal(4, await verify.People.CountAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Switching_genealogy_during_profile_relationship_and_union_edits_keeps_the_original_scoped_transaction()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        var pause = new PauseCommandInterceptor();
        var options = new DbContextOptionsBuilder<ZupuDbContext>()
            .UseSqlite($"Data Source={database.DatabasePath};Pooling=False")
            .AddInterceptors(pause).Options;
        var coordinator = new TargetContextCoordinator(options);
        var vm = new GenealogySelectionViewModel(coordinator.CreateContext, coordinator) { NewName = "甲谱" };
        await vm.CreateAsync();
        var firstGenealogy = vm.SelectedGenealogy!;
        foreach (var name in new[] { "甲人物", "乙人物" })
        {
            vm.NewPersonName = name;
            await vm.AddPersonAsync();
        }
        var firstPerson = vm.People.Single(item => item.Name == "甲人物");
        var firstPeer = vm.People.Single(item => item.Name == "乙人物");
        vm.NewName = "乙谱";
        await vm.CreateAsync();
        var secondGenealogy = vm.SelectedGenealogy!;
        vm.NewPersonName = "外谱人物";
        await vm.AddPersonAsync();

        await vm.SelectAsync(firstGenealogy);
        vm.SelectedPerson = vm.People.Single(item => item.Id == firstPerson.Id);
        vm.EditedBiography = "甲谱档案";
        pause.Arm("UPDATE \"People\"");
        var profile = vm.SavePersonProfileAsync();
        await pause.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        await vm.SelectAsync(secondGenealogy);
        Assert.Null(vm.SelectedPerson);
        pause.Release();
        await profile;
        Assert.Equal(secondGenealogy.Id, vm.SelectedGenealogy!.Id);
        Assert.All(vm.People, item => Assert.Equal(secondGenealogy.Id, item.GenealogyId));

        await vm.SelectAsync(firstGenealogy);
        vm.SelectedPerson = vm.People.Single(item => item.Id == firstPerson.Id);
        pause.Arm("INSERT INTO \"PersonRelationships\"");
        var relationship = vm.AddTargetRelationshipAsync(PersonRelationshipKind.Spouse, firstPeer.Id);
        await pause.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        await vm.SelectAsync(secondGenealogy);
        pause.Release();
        await relationship;
        Assert.Equal(secondGenealogy.Id, vm.SelectedGenealogy!.Id);
        Assert.All(vm.People, item => Assert.Equal(secondGenealogy.Id, item.GenealogyId));

        await vm.SelectAsync(firstGenealogy);
        vm.SelectedPerson = vm.People.Single(item => item.Id == firstPerson.Id);
        pause.Arm("INSERT INTO \"FamilyUnions\"");
        var union = vm.AddTargetFamilyUnionAsync(FamilyUnionKind.Marriage, firstPeer.Id);
        await pause.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        await vm.SelectAsync(secondGenealogy);
        pause.Release();
        await union;
        Assert.Equal(secondGenealogy.Id, vm.SelectedGenealogy!.Id);
        Assert.All(vm.People, item => Assert.Equal(secondGenealogy.Id, item.GenealogyId));

        await vm.SelectAsync(firstGenealogy);
        vm.SelectedPerson = vm.People.Single(item => item.Id == firstPerson.Id);
        var removableUnion = (await vm.GetTargetLinkRemovalCandidatesAsync())
            .Single(item => item.Kind == TargetLinkRemovalKind.FamilyUnion);
        pause.Arm("DELETE FROM \"FamilyUnions\"");
        var removal = vm.RemoveTargetLinkAsync(removableUnion);
        await pause.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        await vm.SelectAsync(secondGenealogy);
        pause.Release();
        await removal;

        await using var verify = database.CreateContext();
        Assert.Equal("甲谱档案", (await verify.People.SingleAsync(item => item.Id == firstPerson.Id)).Biography);
        Assert.Equal(1, await verify.PersonRelationships.CountAsync(item => item.GenealogyId == firstGenealogy.Id));
        Assert.Equal(0, await verify.FamilyUnions.CountAsync(item => item.GenealogyId == firstGenealogy.Id));
        Assert.Equal(0, await verify.PersonRelationships.CountAsync(item => item.GenealogyId == secondGenealogy.Id));
        Assert.Equal(0, await verify.FamilyUnions.CountAsync(item => item.GenealogyId == secondGenealogy.Id));
        Assert.All(vm.People, item => Assert.Equal(secondGenealogy.Id, item.GenealogyId));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Failed_target_link_removal_keeps_all_graph_records_intact()
    {
        await using var database = new TestDatabase();
        await database.MigrateAsync();
        await using var context = database.CreateContext();
        var service = new GenealogyDataService(context);
        var genealogy = await service.CreateGenealogyAsync(new Genealogy { Name = "失败解除谱" });
        var first = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "甲" });
        var second = await service.AddPersonAsync(new Person { GenealogyId = genealogy.Id, Name = "乙" });
        var relationship = await service.AddRelationshipAsync(new PersonRelationship
        {
            GenealogyId = genealogy.Id, PersonId = first.Id, RelatedPersonId = second.Id, Kind = PersonRelationshipKind.Spouse
        });
        var union = await service.AddFamilyUnionAsync(new FamilyUnion
        {
            GenealogyId = genealogy.Id, PartnerOneId = first.Id, PartnerTwoId = second.Id
        });
        await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectTargetRelationshipDelete BEFORE DELETE ON PersonRelationships BEGIN SELECT RAISE(ABORT, 'simulated relationship delete failure'); END;");
        await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectTargetUnionDelete BEFORE DELETE ON FamilyUnions BEGIN SELECT RAISE(ABORT, 'simulated union delete failure'); END;");

        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => service.RemoveRelationshipForParticipantAsync(genealogy.Id, relationship.Id, first.Id));
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => service.RemoveFamilyUnionForParticipantAsync(genealogy.Id, union.Id, first.Id));
        await using var verify = database.CreateContext();
        Assert.Equal(2, await verify.People.CountAsync());
        Assert.Single(await verify.PersonRelationships.ToListAsync());
        Assert.Single(await verify.FamilyUnions.ToListAsync());
    }

    private sealed class PauseCommandInterceptor : DbCommandInterceptor
    {
        private string? _sqlFragment;
        private int _armed;
        private TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource _continue = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => _entered.Task;

        public void Arm(string sqlFragment)
        {
            _sqlFragment = sqlFragment;
            _entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _continue = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref _armed, 1);
        }

        public void Release() => _continue.TrySetResult();

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            await PauseIfArmedAsync(command, cancellationToken);
            return result;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await PauseIfArmedAsync(command, cancellationToken);
            return result;
        }

        private async Task PauseIfArmedAsync(DbCommand command, CancellationToken cancellationToken)
        {
            var fragment = _sqlFragment;
            if (fragment is null || !command.CommandText.Contains(fragment, StringComparison.Ordinal) ||
                Interlocked.Exchange(ref _armed, 0) != 1) return;
            var entered = _entered;
            var continuation = _continue;
            entered.TrySetResult();
            await continuation.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class FailRefreshAfterWriteInterceptor : DbCommandInterceptor
    {
        private string? _writeFragment;
        private int _failReads;

        public void Arm(string writeFragment)
        {
            _writeFragment = writeFragment;
            Volatile.Write(ref _failReads, 0);
        }

        public void Disable()
        {
            _writeFragment = null;
            Volatile.Write(ref _failReads, 0);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            ThrowWhenRefreshReads(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            ArmRefreshFailureAfterWrite(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            ArmRefreshFailureAfterWrite(command);
            return ValueTask.FromResult(result);
        }

        private void ArmRefreshFailureAfterWrite(DbCommand command)
        {
            var writeFragment = _writeFragment;
            if (writeFragment is not null && command.CommandText.Contains(writeFragment, StringComparison.Ordinal))
                Volatile.Write(ref _failReads, 1);
        }

        private void ThrowWhenRefreshReads(DbCommand command)
        {
            if (Volatile.Read(ref _failReads) == 1 && command.CommandText.Contains("SELECT", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("simulated refresh read failure");
        }
    }
}
