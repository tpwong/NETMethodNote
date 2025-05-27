<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <location path="." inheritInChildApplications="false">
    <system.webServer>
      <handlers>
        <add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
      </handlers>
      <aspNetCore processPath="dotnet"
                  arguments=".\YourAppName.dll"
                  stdoutLogEnabled="true"
                  stdoutLogFile=".\logs\stdout"
                  hostingModel="inprocess">
        <environmentVariables>
          <!-- 启用开发环境以显示详细错误 -->
          <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Development" />
          <!-- 启用详细错误显示 -->
          <environmentVariable name="ASPNETCORE_DETAILEDERRORS" value="1" />
          <!-- 启用IIS集成诊断日志 -->
          <environmentVariable name="ASPNETCORE_HOSTINGSTARTUPASSEMBLIES" value="Microsoft.AspNetCore.Diagnostics.IIS" />
          <!-- 启用服务器时间测量 -->
          <environmentVariable name="DOTNET_ShowTimestamps" value="1" />
          <!-- 启用Kestrel详细日志 -->
          <environmentVariable name="Logging__LogLevel__Microsoft.AspNetCore.Server.Kestrel" value="Trace" />
        </environmentVariables>
      </aspNetCore>
      
      <!-- 配置详细HTTP错误 -->
      <httpErrors errorMode="Detailed" existingResponse="PassThrough">
        <clear />
      </httpErrors>
      
      <!-- 启用失败请求跟踪 -->
      <tracing>
        <traceFailedRequests>
          <add path="*">
            <traceAreas>
              <add provider="ASP" verbosity="Verbose" />
              <add provider="ASPNET" areas="Infrastructure,Module,Page,AppServices" verbosity="Verbose" />
              <add provider="ISAPI Extension" verbosity="Verbose" />
              <add provider="WWW Server" areas="Authentication,Security,Filter,StaticFile,CGI,Compression,Cache,RequestNotifications,Module,FastCGI" verbosity="Verbose" />
            </traceAreas>
            <failureDefinitions statusCodes="400-599" />
          </add>
        </traceFailedRequests>
      </tracing>
      
      <!-- 增加响应缓冲区大小，以容纳更大的错误页面 -->
      <serverRuntime uploadReadAheadSize="8388608" />
    </system.webServer>
  </location>
</configuration>