using System.Windows;
using System.Windows.Controls;
using PuJiang.V1.Application;

namespace ZupuPaiBan.Views;

internal sealed class V1RootEditDialog : Window
{
    private bool _saving,_uncertain;
    internal V1RootEditDialog(FirstRootApplication app,Guid g,Guid l,RootNode root)
    {
        Title="修改首节点信息"; Width=420; Height=400; WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var gen=new TextBox{Text=root.Generation?.ToString()??""};var title=new TextBox{Text=root.Title};var notes=new TextBox{Text=root.Notes};var panel=new StackPanel{Margin=new Thickness(16)};
        void Add(string label,TextBox box){panel.Children.Add(new TextBlock{Text=label,Margin=new Thickness(0,8,0,2)});panel.Children.Add(box);}
        panel.Children.Add(new TextBlock{Text="姓名可在人物资料中修改。"});Add("主世系（以房头为一世，可留空）",gen);Add("称谓",title);Add("备注",notes);
        var status=new TextBlock{Foreground=System.Windows.Media.Brushes.Firebrick,TextWrapping=TextWrapping.Wrap};var save=new Button{Content="保存",Padding=new Thickness(8),Margin=new Thickness(0,10,0,0)};panel.Children.Add(save);panel.Children.Add(status);UpdateRootDetails? pending=null;
        save.Click+=async(_,_)=>{if(_saving)return;int? generation=null;if(!string.IsNullOrWhiteSpace(gen.Text)){if(!int.TryParse(gen.Text,out var n)||n<=0){status.Text="主世系请输入正整数。";return;}generation=n;}pending??=new(Guid.NewGuid(),g,l,root.PersonId,root.PlacementId,generation,title.Text.Trim(),notes.Text.Trim(),root.Generation,root.Title,root.Notes);_saving=true;save.IsEnabled=false;
            try{await app.UpdateDetailsAsync(pending);_saving=false;_uncertain=false;DialogResult=true;}
            catch(Exception error){try{if(!await app.HasDetailsReceiptAsync(pending.OperationId)){pending=null;_uncertain=false;status.Text=$"未保存：{error.Message}，输入已保留。";}else{_uncertain=true;status.Text="保存结果待确认，请重试。";}}catch{_uncertain=true;status.Text="保存结果未确认，请重试。";}foreach(var box in new[]{gen,title,notes})box.IsReadOnly=_uncertain;}
            finally{_saving=false;save.IsEnabled=true;}};
        Closing+=(_,e)=>{if(_saving||_uncertain){e.Cancel=true;status.Text="保存尚未确认，请重试。";}};Content=panel;
    }
}
internal sealed class V1LineageEditDialog : Window
{
    private bool _saving, _uncertain;
    internal V1LineageEditDialog(LineageApplication app, Guid genealogy, LineageHome home)
    {
        Title="修改谱系资料"; Width=440; Height=620; WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var name=new TextBox { Text=home.Name }; var category=new TextBox { Text=home.Category }; var region=new TextBox { Text=home.Region }; var topic=new TextBox { Text=home.Topic }; var hall=new TextBox { Text=home.Hall }; var notes=new TextBox { Text=home.Notes };
        var fields=new StackPanel {Margin=new Thickness(16)}; fields.Children.Add(new TextBlock {Text="项目主姓氏："+home.Surname});
        void Add(string label, TextBox box) {fields.Children.Add(new TextBlock {Text=label,Margin=new Thickness(0,8,0,2)});fields.Children.Add(box);}
        Add("谱系名称（必填）",name);Add("所属分类（必填）",category);Add("地区（必填）",region);Add("主题名（必填）",topic);Add("郡望堂号",hall);Add("备注",notes);
        var status=new TextBlock {TextWrapping=TextWrapping.Wrap,Foreground=System.Windows.Media.Brushes.Firebrick};var save=new Button {Content="保存",Padding=new Thickness(8),Margin=new Thickness(0,12,0,0)};fields.Children.Add(save);fields.Children.Add(status);UpdateLineage? pending=null;
        save.Click += async (_,_)=> {if(_saving)return;if(new[]{name.Text,category.Text,region.Text,topic.Text}.Any(string.IsNullOrWhiteSpace)){status.Text="请填写所有必填项。";return;}pending ??=new(Guid.NewGuid(),genealogy,home.Id,name.Text.Trim(),category.Text.Trim(),region.Text.Trim(),topic.Text.Trim(),hall.Text.Trim(),notes.Text.Trim(),LineageApplication.Evidence(home));_saving=true;save.IsEnabled=false;
            try{await app.UpdateAsync(pending);_saving=false;_uncertain=false;DialogResult=true;}
            catch(Exception error){try{if(!await app.HasUpdateReceiptAsync(pending.OperationId)){pending=null;_uncertain=false;status.Text=$"未保存：{error.Message}，输入已保留。";}else{_uncertain=true;status.Text="保存结果待确认，请重试。";}}catch{_uncertain=true;status.Text="保存结果未确认，请重试。";}foreach(var box in new[]{name,category,region,topic,hall,notes})box.IsReadOnly=_uncertain;}
            finally{_saving=false;save.IsEnabled=true;}};
        Closing+=(_,e)=>{if(_saving||_uncertain){e.Cancel=true;status.Text="保存尚未确认，请重试。";}};
        Content=new ScrollViewer {Content=fields,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    }
}
internal sealed class V1LineageCreateDialog : Window
{
    private readonly TextBox _name=new(), _category=new(), _region=new(), _topic=new(), _hall=new(), _notes=new();
    internal CreateLineage? Command { get; private set; }
    internal Guid CreatedId { get; private set; }
    private bool _saving, _uncertain;
    internal V1LineageCreateDialog(LineageApplication app, Guid genealogy, string surname)
    {
        Title="新建谱系"; Width=440; Height=620; WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var fields=new StackPanel { Margin=new Thickness(16) }; fields.Children.Add(new TextBlock { Text="项目主姓氏："+surname });
        void Add(string label,TextBox box) { fields.Children.Add(new TextBlock { Text=label, Margin=new Thickness(0,8,0,2) }); fields.Children.Add(box); }
        Add("谱系名称（必填）",_name); Add("所属分类（必填）",_category); Add("地区（必填）",_region); Add("主题名（必填）",_topic); Add("郡望堂号",_hall); Add("备注",_notes);
        var status=new TextBlock { TextWrapping=TextWrapping.Wrap, Foreground=System.Windows.Media.Brushes.Firebrick };
        var save=new Button { Content="创建空谱", Margin=new Thickness(0,12,0,0), Padding=new Thickness(8) }; fields.Children.Add(save); fields.Children.Add(status);
        save.Click += async (_,_) => { if(_saving)return; if(new[]{_name.Text,_category.Text,_region.Text,_topic.Text,surname}.Any(string.IsNullOrWhiteSpace)) {status.Text="请填写所有必填项；姓氏由项目资料提供。";return;} Command ??= new(Guid.NewGuid(),genealogy,_name.Text.Trim(),_category.Text.Trim(),_region.Text.Trim(),_topic.Text.Trim(),_hall.Text.Trim(),_notes.Text.Trim()); _saving=true; save.IsEnabled=false;
            try { CreatedId=await app.CreateAsync(Command); _uncertain=false; _saving=false; DialogResult=true; }
            catch(Exception error) { try { if(!await app.HasReceiptAsync(Command.OperationId)) { Command=null; _uncertain=false; status.Text=$"未保存：{error.Message}，输入已保留。"; } else { _uncertain=true; status.Text="保存结果待确认，请重试同一操作。"; } } catch { _uncertain=true; status.Text="保存结果未确认，请重试同一操作。"; } foreach(var box in new[]{_name,_category,_region,_topic,_hall,_notes}) box.IsReadOnly=_uncertain; }
            finally { _saving=false; save.IsEnabled=true; } };
        Closing += (_,e) => { if(_saving||_uncertain) {e.Cancel=true;status.Text="保存尚未确认，请重试。";} };
        Content=new ScrollViewer { Content=fields, VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
    }
}
internal sealed class V1RootDetailsDialog : Window
{
    private readonly TextBox _name=new(), _generation=new(), _title=new(), _notes=new();
    internal AddFirstRoot? Command { get; private set; }
    internal RootCreated? Created { get; private set; }
    private bool _saving, _uncertain;
    internal V1RootDetailsDialog(FirstRootApplication app, Guid g, Guid l, string surname)
    {
        Title="录入首节点（可取消）"; Width=420; Height=460; WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new StackPanel { Margin=new Thickness(16) }; panel.Children.Add(new TextBlock { Text="姓氏："+surname });
        void Add(string label,TextBox box) { panel.Children.Add(new TextBlock { Text=label, Margin=new Thickness(0,8,0,2) }); panel.Children.Add(box); }
        Add("谱名",_name); Add("主世系（以房头为一世，可留空）",_generation); Add("称谓",_title); Add("备注",_notes);
        var status=new TextBlock { Foreground=System.Windows.Media.Brushes.Firebrick, TextWrapping=TextWrapping.Wrap }; var save=new Button { Content="保存首节点", Padding=new Thickness(8), Margin=new Thickness(0,10,0,0) }; var cancel=new Button { Content="暂不录入", Padding=new Thickness(8) }; panel.Children.Add(save); panel.Children.Add(cancel); panel.Children.Add(status);
        save.Click += async (_,_) => { if(_saving)return; if(string.IsNullOrWhiteSpace(_name.Text)) {status.Text="请填写谱名；取消不会创建人物。";return;} int? gen=null; if(!string.IsNullOrWhiteSpace(_generation.Text)) {if(!int.TryParse(_generation.Text,out var n)||n<=0){status.Text="主世系请输入正整数。";return;}gen=n;} Command ??= new(Guid.NewGuid(),g,l,RootNameMode.Named,surname+_name.Text.Trim(),gen,_title.Text.Trim(),_notes.Text.Trim()); _saving=true;save.IsEnabled=false; cancel.IsEnabled=false;
            try { Created=await app.ExecuteAsync(Command); _uncertain=false;_saving=false;DialogResult=true; }
            catch(Exception error) { try {if((await app.ReadAsync(g,l)).Nodes.Count==0){Command=null;_uncertain=false;status.Text=$"未保存：{error.Message}，输入已保留。";}else{_uncertain=true;status.Text="保存结果待确认，请重试同一操作。";}}catch{_uncertain=true;status.Text="保存结果未确认，请重试同一操作。";} foreach(var box in new[]{_name,_generation,_title,_notes})box.IsReadOnly=_uncertain; }
            finally { _saving=false;save.IsEnabled=true;cancel.IsEnabled=!_uncertain; } };
        Closing += (_,e) => { if(_saving||_uncertain){e.Cancel=true;status.Text="保存尚未确认，请重试。";} };
        cancel.Click += (_,_) => DialogResult=false; Content=panel;
    }
}
