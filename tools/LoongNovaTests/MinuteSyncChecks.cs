using DDEN.LoongNova;
using Newtonsoft.Json.Linq;
internal static class MinuteSyncChecks
{
    internal static void Run(Action<bool,string> check)
    {
        foreach(double frame in new[]{0.02,0.1,1.0,5.0})
        foreach(int rate in new[]{1,16})
        {
            var state=new BeltPowerState();state.Load(1000,true);
            var activity=new BeltActivitySession();var budget=new WorkReportBudget();
            check(activity.Report(state,budget,state.Epoch,0,rate,0),"A start event establishes a server-side energy clock");
            double elapsed=0;
            while(elapsed<60){elapsed=Math.Min(60,elapsed+frame);activity.Tick(state,elapsed,true);}
            check(Math.Abs(state.Remaining-(1000-rate*30))<0.002,"The host deducts a full minute of activity without interim packets");
            long revision=state.Revision;
            check(activity.Report(state,budget,state.Epoch,rate*60,rate,60.2)&&state.Revision>revision,
                "Minute checkpoints reconcile latency and preserve increasing revisions");
            activity.Tick(state,65.2,true);
            check(activity.Report(state,budget,state.Epoch,rate*65,0,65.3)&&!activity.Running
                &&Math.Abs(state.Remaining-(1000-rate*32.5))<0.002,
                "The stop event settles exact cumulative consumption rather than retaining clock prediction error");
            float stopped=state.Remaining;activity.Tick(state,100,true);
            check(state.Remaining==stopped,"Stopped sessions never continue consuming energy");
            string before=state.Capture().ToString();
            check(!activity.Report(state,budget,state.Epoch,100000,16,101)&&state.Capture().ToString()==before,
                "Impossible cumulative reports must fail atomically without resetting energy");
        }
        var abandoned=new BeltPowerState();abandoned.Load(1000,false);
        var lease=new BeltActivitySession();var allowance=new WorkReportBudget();
        lease.Report(abandoned,allowance,abandoned.Epoch,0,16,0);lease.Tick(abandoned,100,true);
        check(!lease.Running&&Math.Abs(abandoned.Remaining-480)<0.001,"An abandoned stream expires after 65 seconds rather than draining forever");
        var disabled=new BeltPowerState();disabled.Load(1000,false);lease=new BeltActivitySession();allowance=new WorkReportBudget();
        lease.Report(disabled,allowance,disabled.Epoch,0,16,0);lease.Tick(disabled,1,false);float remaining=disabled.Remaining;
        lease.Tick(disabled,60,true);check(disabled.Remaining==remaining,"Ownership loss or disabling terminates the host's active consumption clock");
        var request=new JObject{["operation"]="read",["knownRevision"]=7};
        var unchanged=new JObject{["ok"]=true,["state"]=new JObject{["revision"]=7,["energy"]=123}};
        StateDelta.Compact(unchanged,request);check(unchanged["state"]==null&&unchanged.Value<bool>("unchanged"),"Unchanged reads return a small cache acknowledgement without the full state");
        var changed=new JObject{["ok"]=true,["state"]=new JObject{["revision"]=8}};
        StateDelta.Compact(changed,request);check(changed["state"]!=null,"Changed revisions must always return the authoritative snapshot");
        changed["energyMode"]=2;changed["state"]["revision"]=7;
        StateDelta.Compact(changed,request);check(changed["state"]!=null,"Activity-mode changes cannot be hidden behind an unchanged energy revision");
    }
}
