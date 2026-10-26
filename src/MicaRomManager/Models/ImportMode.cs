namespace MicaRomManager.Models;

/// <summary>入库方式：仅记录路径 / 复制到库目录 / 移动到库目录。</summary>
public enum ImportMode
{
    Link,
    Copy,
    Move,
}
