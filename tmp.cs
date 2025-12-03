@startuml
title GSI Known Rating Flow (Timeline View)
scale 100 as 50 pixels

' Define the participants on the Y-axis
robust "Player" as P
robust "WDTS" as W
robust "GSI" as G

' Define the flow over time (X-axis)
@0
P is Idle
W is Idle
G is Idle

' == Anonymous Ratings ==
@10
P is "Seated (Anonymous)"
P -> W : Play rating #1 (anon)
@20
P -> W : Play rating #2 (anon)
@30
P -> W : Play rating #3 (anon)
W is "Storing Anon Ratings"

' Note for the anonymous phase
highlight 10 to 40 #LightBlue;line:DimGrey : Anonymous Phase

' == Player Clock-in ==
@40
P is "Clocking In"
P -> W : Give player-card (clock-in)
W is "Processing Clock-in"

@50
W -> G : Send empty open rating #1 (sessionId: 123)
G is "Session Open"

@60
W -> G : Send update rating #1 (sessionId: 123)

@70
W -> G : Send update rating #2 (sessionId: 123)

@80
W -> G : Send update rating #3 (sessionId: 123)

' == Known Play ==
@90
P is "Playing (Known)"
P -> W : Play rating #4 (sessionId: 123)
W -> G : Send update rating #4

@100
P -> W : Play rating #5 (sessionId: 123)
W -> G : Send update rating #5

' == Player Clock-out ==
@110
P is "Clocking Out"
W -> G : Send close rating (sessionId: 123)
G is Idle
W is Idle
P is Idle

@enduml