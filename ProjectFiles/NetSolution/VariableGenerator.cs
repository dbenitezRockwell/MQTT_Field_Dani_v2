#region Using directives
using System;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;
using FTOptix.NativeUI;
using FTOptix.NetLogic;
using FTOptix.HMIProject;
using FTOptix.DataLogger;
using FTOptix.UI;
using FTOptix.Alarm;
using FTOptix.EventLogger;
using FTOptix.SQLiteStore;
using FTOptix.Store;
using FTOptix.Modbus;
using FTOptix.CoreBase;
using FTOptix.CommunicationDriver;
using FTOptix.Core;
#endregion

public class VariableGenerator : BaseNetLogic
{
    private PeriodicTask taskPeriodico;
    public override void Start()
    {
        // Insert code to be executed when the user-defined logic is started
        taskPeriodico = new PeriodicTask(randomNum, 1500, LogicObject);
        taskPeriodico.Start();
    }

    public void randomNum()
    {
        Random r = new Random();
        if ((bool)Project.Current.GetVariable("Model/RandomEn").Value)
        {
            Project.Current.GetVariable("Model/Tags2MQTT/Variable1").Value = r.Next(0, 100);
            Project.Current.GetVariable("Model/Tags2MQTT/Variable2").Value = r.Next(0, 100);
            Project.Current.GetVariable("Model/Tags2MQTT/Variable3").Value = r.Next(0, 100);
        }
    }

    public override void Stop()
    {
        // Insert code to be executed when the user-defined logic is stopped
        taskPeriodico.Dispose();
    }
}
