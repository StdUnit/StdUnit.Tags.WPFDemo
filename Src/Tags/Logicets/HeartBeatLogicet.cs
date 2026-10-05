using StdUnit.Tags;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WPFDemo.Tags.Logicets;

public class HeartBeatLogicet : LogicetBase
{
    private readonly ITag heartReq;
    private readonly ITag heartAck;
    private readonly ILogger<HeartBeatLogicet> _logger;

    public HeartBeatLogicet(IReadOnlyList<ITagChannel> channels, ITagGrp tags, ILogger<HeartBeatLogicet> logger) : base(channels, tags)
    {
        var grp = tags.SelectGrp("IoBox/通用状态");

        heartReq = grp.SelectTag("PLC/心跳请求");
        heartAck = grp.SelectTag("MST/心跳响应");
        this._logger = logger;
    }

    public override int Order => 2;

    public override bool MatchEntry(ITagGrp entry) => entry.TagName()== "IoBox";

    public override Task ProcessAsync(ITagGrp entry, ITagChannel? thisChannel)
    {
        var now = Stopwatch.GetTimestamp();
        heartAck.Value = heartReq.Value;
        return Task.CompletedTask;
    }
}