// The Multiplayer bridge is a separate assembly on purpose (#163): its packet types
// implement an MPAPI interface and would take the whole mod down with them whenever
// the Multiplayer mod is absent. It still needs the channel's internal entry points,
// so it is the one assembly allowed to see them.
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("DleMpBridge")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("DleSignalsBridge")]
