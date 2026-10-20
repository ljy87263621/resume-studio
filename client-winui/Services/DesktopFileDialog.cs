using System.Runtime.InteropServices;

namespace ResumeStudio.Services;

/// <summary>Desktop Common Item Dialog: path results without a WinRT picker broker or package identity.</summary>
public static class DesktopFileDialog
{
    private const int Cancelled=unchecked((int)0x800704C7);
    public static string? Open(nint owner,string title,string[] extensions,CancellationToken cancellationToken=default,string? suggestedFileName=null)=>Show(owner,title,extensions,suggestedFileName,false,cancellationToken);
    public static string? Save(nint owner,string title,string[] extensions,string suggestedName,CancellationToken cancellationToken=default)=>Show(owner,title,extensions,suggestedName,true,cancellationToken);
    private static string? Show(nint owner,string title,string[] extensions,string? name,bool save,CancellationToken token)
    {
        if(owner==0)throw new ArgumentException("文件窗口缺少所属窗口。",nameof(owner));
        if(extensions.Length==0||extensions.Any(e=>!System.Text.RegularExpressions.Regex.IsMatch(e,"^\\.[a-zA-Z0-9]+$")))throw new ArgumentException("文件类型无效。",nameof(extensions));
        if(token.IsCancellationRequested)return null;
        var type=Type.GetTypeFromCLSID(new Guid(!save?"DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7":"C0B4E2F3-BA21-4773-8DBA-335EC946EB8B"),throwOnError:true)!;
        var dialog=(IFileDialog)Activator.CreateInstance(type)!;
        try
        {
            dialog.SetTitle(title);dialog.GetOptions(out uint options);
            dialog.SetOptions(options|0x40u|0x8u|0x800u|(!save?0x1000u:0x2u));
            dialog.SetFileTypes(1,[new Filter {Name=title,Pattern=string.Join(";",extensions.Select(e=>"*"+e))}]);
            dialog.SetDefaultExtension(extensions[0][1..]);if(name is not null)dialog.SetFileName(name);
            using var registration=token.Register(()=> {try{dialog.Close(Cancelled);}catch(COMException){}});
            int result=dialog.Show(owner);if(result==Cancelled||token.IsCancellationRequested)return null;
            if(result<0)throw new IOException($"无法打开文件窗口（0x{result:X8}）。",Marshal.GetExceptionForHR(result));
            dialog.GetResult(out var item);
            try {item.GetDisplayName(0x80058000,out nint text);try{return Marshal.PtrToStringUni(text);}finally{Marshal.FreeCoTaskMem(text);}}
            finally{Marshal.ReleaseComObject(item);}
        }
        finally{Marshal.ReleaseComObject(dialog);}
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    private struct Filter { [MarshalAs(UnmanagedType.LPWStr)]public string Name;[MarshalAs(UnmanagedType.LPWStr)]public string Pattern; }
    [ComImport,Guid("42F85136-DB7E-439C-85F1-E4075D135FC8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileDialog
    {
        [PreserveSig]int Show(nint owner);
        void SetFileTypes(uint count,[In,MarshalAs(UnmanagedType.LPArray,SizeParamIndex=0)]Filter[] filters);
        void SetFileTypeIndex(uint index);void GetFileTypeIndex(out uint index);
        void Advise(nint events,out uint cookie);void Unadvise(uint cookie);
        void SetOptions(uint options);void GetOptions(out uint options);
        void SetDefaultFolder(nint item);void SetFolder(nint item);void GetFolder(out nint item);void GetCurrentSelection(out nint item);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)]string name);void GetFileName(out nint name);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)]string title);void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)]string text);void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)]string text);
        void GetResult(out IShellItem item);void AddPlace(nint item,int placement);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)]string extension);
        void Close(int result);void SetClientGuid(in Guid guid);void ClearClientData();void SetFilter(nint filter);
    }
    [ComImport,Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(nint context,in Guid handler,in Guid iid,out nint result);void GetParent(out nint parent);
        void GetDisplayName(uint kind,out nint name);void GetAttributes(uint mask,out uint attributes);void Compare(nint other,uint hint,out int order);
    }
}
