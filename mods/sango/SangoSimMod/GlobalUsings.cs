// sango 源码以 UnityEngine.Vector* 表达坐标;PLAN D2 裁定统一映射到 System.Numerics。
// (已核对:Object/Json 层只构造/传递 Vector3,小写成员访问均落在 sango 自有类型上;
//  例外是 Framework/Hex/Hex.cs 两处 pos.x/pos.z,已改为 pos.X/pos.Z。)
global using Vector2 = System.Numerics.Vector2;
global using Vector3 = System.Numerics.Vector3;
global using Vector4 = System.Numerics.Vector4;

// sango 的 Newtonsoft vendor 在 Extensions/ 下、命名空间为 TKNewtonsoft.*(D8 不搬)。
// C# 的 using 指令不允许别名做限定符(global using TKNewtonsoft = Newtonsoft 也救不了
// 文件级 using TKNewtonsoft.Json;),因此移植时把源文件里的 TKNewtonsoft 统一改写为
// NuGet Newtonsoft.Json(版本锁定 external/nuget 在库版本)。代码体与类型语义不变。
