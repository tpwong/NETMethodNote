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
          <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Development" />
          <environmentVariable name="ASPNETCORE_DETAILEDERRORS" value="1" />
        </environmentVariables>
      </aspNetCore>
      <!-- 显示详细错误 -->
      <httpErrors errorMode="Detailed" existingResponse="PassThrough">
        <clear />
      </httpErrors>
    </system.webServer>
  </location>
</configuration>