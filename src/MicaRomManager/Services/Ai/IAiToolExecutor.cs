namespace MicaRomManager.Services.Ai;

/// <summary>工具执行器抽象；便于给助手服务注入假实现测试取消与失败路径。</summary>
public interface IAiToolExecutor
{
    Task<AiToolExecution> ExecuteAsync(AiToolCall call, IAiAppHost host, CancellationToken token);
}
