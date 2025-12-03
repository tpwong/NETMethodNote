@startuml
title GSI Known Rating Flow (Timeline View)
skinparam backgroundColor white

' == 1. 定義 Y 軸的角色 (這就是你要的 Y 軸) ==
' concise 模式會讓每個角色變成一條平整的時間帶
concise "Player" as P
concise "WDTS" as W
concise "GSI" as G

' == 2. 定義時間軸與狀態 (這就是 X 軸) ==

@0
P is Idle
W is Idle
G is Idle

' -- 匿名階段 (Anonymous) --
@100
P is "Seated (Anon)" #LightBlue
W is "Local Storage" #LightBlue
P -> W : Play rating #1 (anon)

@200
P -> W : Play rating #2 (anon)

@300
P -> W : Play rating #3 (anon)

' -- 插卡登入 (Clock-in) --
@400
P is "Clock-in" #LightGreen
W is "Processing" #LightGreen
P -> W : Give Card (Clock-in)

@450
W -> G : Send Empty Open #1 (123)
G is "Session Open" #LightGreen

' -- 同步舊資料 (Syncing) --
@500
W is "Syncing"
W -> G : Send update #1

@600
W -> G : Send update #2

@700
W -> G : Send update #3

' -- 實名遊玩 (Live Play) --
@800
P is "Known Play" #Gold
W is "Real-time" #Gold
P -> W : Play rating #4
W -> G : Update #4

@900
P -> W : Play rating #5
W -> G : Update #5

' -- 登出 (Clock-out) --
@1000
P is Idle
W is Idle
W -> G : Close rating
G is Idle

' == 加上標註說明 ==
note bottom of W : Ratings #1-3 stored locally initially\nthen synced after clock-in.

@enduml