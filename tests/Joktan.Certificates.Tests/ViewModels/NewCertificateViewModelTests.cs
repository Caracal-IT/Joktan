using System;
using System.Linq;
using System.Threading.Tasks;
using Joktan.Certificates.ViewModels;
using Xunit;

namespace Joktan.Certificates.Tests.ViewModels;

public class NewCertificateViewModelTests : CertificateTestBase
{
    [Fact]
    public void A_root_ca_has_one_kind_and_signs_itself()
    {
        var vm = new NewCertificateViewModel(Store, null);

        Assert.Equal("New root CA", vm.Title);
        Assert.StartsWith("Itself.", vm.IssuerText);
        Assert.False(vm.CanChooseKind);
        Assert.Equal(CertificateKind.RootAuthority, vm.SelectedKind.Value);
        Assert.False(vm.ShowAlternativeNames);
        Assert.Contains("Signs itself", vm.KindHint);
        Assert.Null(vm.LastDate);
        Assert.Equal(DateTime.Today.AddDays(1), vm.FirstDate);
        Assert.Equal(Store.AuthorityFolder, vm.Folder);
        Assert.StartsWith("Its files go in ", vm.FolderPreview);
        Assert.Null(vm.Error);
        Assert.Equal(4, vm.KeyAlgorithms.Count);
        Assert.Equal("Root CA", vm.Kinds[0].ToString());
    }

    [Fact]
    public void A_certificate_takes_after_the_ca_that_signs_it()
    {
        var root = Store.Create(new CertificateSpec
        {
            Kind = CertificateKind.RootAuthority,
            Name = "root",
            CommonName = "root",
            Organization = "Org",
            OrganizationalUnit = "Unit",
            Locality = "Town",
            State = "State",
            Country = "ZA",
            NotAfter = DateTimeOffset.UtcNow.AddYears(1),
        });

        var vm = new NewCertificateViewModel(Store, root, CertificateKind.Client);

        Assert.Equal("New certificate signed by root", vm.Title);
        Assert.Contains("root (Root CA), valid until ", vm.IssuerText);
        Assert.True(vm.CanChooseKind);
        Assert.Equal(CertificateKind.Client, vm.SelectedKind.Value);
        Assert.Equal("Org", vm.Organization);
        Assert.Equal("Unit", vm.OrganizationalUnit);
        Assert.Equal("Town", vm.Locality);
        Assert.Equal("State", vm.State);
        Assert.Equal("ZA", vm.Country);
        Assert.Equal(root.Certificate!.NotAfter.Date, vm.LastDate);
        Assert.Equal(CertificateStore.ParentOf(root), vm.Folder);
        Assert.Equal("The certificate a client shows to a broker that asks for one (mutual TLS).", vm.KindHint);
        Assert.StartsWith("Optional.", vm.AlternativeNamesHint);
    }

    [Fact]
    public void A_kind_that_is_not_offered_gives_the_first()
    {
        var vm = new NewCertificateViewModel(Store, Root(), CertificateKind.RootAuthority);

        Assert.Equal(CertificateKind.Server, vm.SelectedKind.Value);
        Assert.Contains("broker shows", vm.KindHint);
        Assert.Contains("host names", vm.AlternativeNamesHint);
    }

    [Fact]
    public void The_defaults_follow_the_kind_unless_they_were_changed()
    {
        var root = Root();
        var vm = new NewCertificateViewModel(Store, root, CertificateKind.Server);
        var defaults = Store.Defaults;
        Assert.Equal(defaults.CommonNameFor(CertificateKind.Server), vm.CommonName);
        Assert.Equal(defaults.ServerAlternativeNames, vm.AlternativeNames);

        vm.SelectedKind = vm.Kinds.First(k => k.Value == CertificateKind.Client);
        Assert.Equal(defaults.CommonNameFor(CertificateKind.Client), vm.CommonName);
        Assert.Equal("", vm.AlternativeNames);

        vm.SelectedKind = vm.Kinds.First(k => k.Value == CertificateKind.IntermediateAuthority);
        Assert.Contains("A CA signed by this one", vm.KindHint);
        Assert.Equal(defaults.CommonNameFor(CertificateKind.IntermediateAuthority), vm.CommonName);

        vm.CommonName = "mine";
        vm.AlternativeNames = "mine.example";
        vm.ValidUntil = DateTime.Today.AddDays(5);
        vm.SelectedKind = vm.Kinds.First(k => k.Value == CertificateKind.Server);
        Assert.Equal("mine", vm.CommonName);
        Assert.Equal("mine.example", vm.AlternativeNames);
        Assert.Equal(DateTime.Today.AddDays(5), vm.ValidUntil);

        vm.CommonName = " ";
        vm.AlternativeNames = "";
        vm.ValidUntil = null;
        vm.SelectedKind = vm.Kinds.First(k => k.Value == CertificateKind.Client);
        Assert.Equal(defaults.CommonNameFor(CertificateKind.Client), vm.CommonName);
        Assert.NotNull(vm.ValidUntil);
    }

    [Fact]
    public void Validity_is_described_and_kept_within_the_ca()
    {
        var root = Root();
        var vm = new NewCertificateViewModel(Store, root, CertificateKind.Client);

        vm.ValidUntil = null;
        Assert.Equal("", vm.ValidityText);
        Assert.Equal("Choose the expiry date", vm.Error);

        vm.ValidUntil = DateTime.Today.AddDays(1);
        Assert.Equal("1 day from today", vm.ValidityText);
        Assert.Null(vm.Error);

        vm.ValidUntil = DateTime.Today.AddDays(7);
        Assert.Equal("7 days from today", vm.ValidityText);

        vm.ValidForCommand.Execute("100");
        Assert.Equal(vm.LastDate, vm.ValidUntil);
        Assert.EndsWith("the day root expires", vm.ValidityText);
        Assert.Null(vm.Error);

        vm.ValidUntil = vm.LastDate!.Value.AddDays(1);
        Assert.Contains("cannot be after", vm.Error);

        vm.ValidUntil = DateTime.Today;
        Assert.Equal("The expiry date must be after today", vm.Error);

        var free = new NewCertificateViewModel(Store, null);
        free.ValidForCommand.Execute("2");
        Assert.Equal(DateTime.Today.AddYears(2), free.ValidUntil);
    }

    [Fact]
    public void Problems_are_found_as_the_fields_change()
    {
        var root = Root();
        var vm = new NewCertificateViewModel(Store, root, CertificateKind.Server);
        Assert.True(vm.SaveCommand.CanExecute(null));

        vm.Name = "root";
        Assert.NotNull(vm.Error);
        Assert.False(vm.SaveCommand.CanExecute(null));
        vm.Name = "";

        vm.AlternativeNames = "";
        Assert.Equal("Enter at least one host name or IP address the broker is reached by", vm.Error);
        vm.AlternativeNames = "not valid name!";
        Assert.NotNull(vm.Error);
        vm.AlternativeNames = "ok.example";
        Assert.Null(vm.Error);

        vm.Country = "too long";
        Assert.NotNull(vm.Error);
        vm.Country = "";

        vm.Folder = "relative/path";
        Assert.NotNull(vm.Error);
        Assert.Equal("", vm.FolderPreview);
    }

    [Fact]
    public void A_ca_that_cannot_sign_keeps_the_certificate_from_being_made()
    {
        var now = DateTimeOffset.Now;
        var root = WithDates(Root(), now.AddDays(-10), now.AddDays(-1));

        var vm = new NewCertificateViewModel(Store, root, CertificateKind.Client);

        Assert.Contains("expired on", vm.Error);
    }

    [Fact]
    public void Saving_makes_the_certificate_and_closes()
    {
        var vm = new NewCertificateViewModel(Store, null) { Name = " made ", Country = "za", Email = "me@example.com" };
        bool? closed = null;
        vm.CloseRequested += made => closed = made;

        vm.SaveCommand.Execute(null);

        Assert.True(closed);
        Assert.Equal("made", vm.Created!.Name);
        Assert.Contains("C=ZA", vm.Created.Certificate!.Subject);
    }

    [Fact]
    public void A_blank_name_is_made_from_the_common_name_and_saving_with_a_problem_does_nothing()
    {
        var vm = new NewCertificateViewModel(Store, null) { CommonName = "From the common name" };
        vm.SaveCommand.Execute(null);
        Assert.Equal("From the common name", vm.Created!.Name);

        var bad = new NewCertificateViewModel(Store, null);
        bad.ValidUntil = null;
        bad.SaveCommand.Execute(null);
        Assert.Null(bad.Created);
    }

    [Fact]
    public void A_folder_blocked_by_a_file_is_reported_before_saving()
    {
        var file = Folder.Write("a-file", "");
        var vm = new NewCertificateViewModel(Store, null) { Folder = file };

        vm.SaveCommand.Execute(null);

        Assert.Null(vm.Created);
        Assert.NotNull(vm.Error);
    }

    [Fact]
    public void A_ca_whose_key_is_damaged_is_only_found_out_when_saving()
    {
        var root = Root();
        System.IO.File.WriteAllText(root.KeyPath, "not a key");
        var vm = new NewCertificateViewModel(Store, root, CertificateKind.Client);
        Assert.Null(vm.Error);

        vm.SaveCommand.Execute(null);

        Assert.Null(vm.Created);
        Assert.StartsWith("The key of root cannot be read", vm.Error);
    }

    [Fact]
    public void Cancelling_closes_without_making_anything()
    {
        var vm = new NewCertificateViewModel(Store, null);
        bool? closed = null;
        vm.CloseRequested += made => closed = made;

        vm.CancelCommand.Execute(null);

        Assert.False(closed);
        Assert.Null(vm.Created);
        Assert.Empty(Store.Entries);

        // With nobody listening, closing does nothing.
        var alone = new NewCertificateViewModel(Store, null);
        alone.CancelCommand.Execute(null);
        alone.SaveCommand.Execute(null);
        Assert.NotNull(alone.Created);
    }

    [Fact]
    public async Task Browsing_uses_the_folder_picked_if_any()
    {
        var cancel = TestContext.Current.CancellationToken;
        var vm = new NewCertificateViewModel(Store, null);
        var before = vm.Folder;

        await vm.BrowseFolderCommand.ExecuteAsync(null).WaitAsync(cancel);
        vm.PickFolder = () => Task.FromResult<string?>(null);
        await vm.BrowseFolderCommand.ExecuteAsync(null).WaitAsync(cancel);
        Assert.Equal(before, vm.Folder);

        vm.PickFolder = () => Task.FromResult<string?>(Folder.Path);
        await vm.BrowseFolderCommand.ExecuteAsync(null).WaitAsync(cancel);
        Assert.Equal(Folder.Path, vm.Folder);
    }

    [Fact]
    public void A_choice_shows_its_text()
    {
        Assert.Equal("text", new Choice<int>(1, "text").ToString());
    }
}
