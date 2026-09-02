# Core Server

模块化业务内核。服务Windows/Linux/Docker发行。包含认证、资源库、资产、元数据、操作、搜索、任务等边界。

V01-001 仅激活 `AssetLibrary.CoreServer.csproj` 的可编译程序集标记与 ASP.NET Core 10 framework reference；没有入口点、网络监听、数据库访问或业务实现。模块代码必须由后续独立 owner 按 `Modules/<Module>/{Domain,Application,Infrastructure,Contracts}` 添加。
