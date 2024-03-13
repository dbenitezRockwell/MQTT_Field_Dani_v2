#region Using directives
using System;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;
using FTOptix.NetLogic;
using FTOptix.HMIProject;
using FTOptix.OPCUAServer;
using FTOptix.UI;
using FTOptix.NativeUI;
using FTOptix.CoreBase;
using FTOptix.Core;
using FTOptix.Alarm;
using System.Collections.Generic;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;
using FTOptix.DataLogger;
using FTOptix.SQLiteStore;
using FTOptix.Store;
using FTOptix.CommunicationDriver;
using FTOptix.OPCUAClient;
using System.Text;
using System.IO;
using Newtonsoft.Json;
using System.Linq;
#endregion

public class AlarmPublisher : BaseNetLogic, IUAEventObserver
{
    public override void Start()
    {
        InitMqttClient();

        alarmObject = (AlarmController)Owner;
        previousActiveState = GetInitialActiveState();
        eventRegistration = alarmObject.RegisterUAEventObserver(this, OpcUa.ObjectTypes.AlarmConditionType);
    }

    public override void Stop()
    {
        // Insert code to be executed when the user-defined logic is stopped
    }

    public void OnEvent(IUAObject eventNotifier, IUAObjectType eventType, IReadOnlyList<object> args, ulong senderId)
    {
        //var eventArguments = eventType.EventArguments;
        //var currentActiveState = (bool)eventArguments.GetFieldValue(args, "ActiveState/Id");
        //var alarmNode = InformationModel.Get<AlarmController>(eventNotifier.NodeId);

        ////if (!IsAlarmTransitioningFromInactiveState(currentActiveState))
        ////    return;

        //var alarmMessage = ConstructAlarmMessage(eventArguments, args, alarmNode);
        //PublishMessage(alarmMessage);
        ////SendAlarmEmail(alarmNode.BrowseName, alarmMessage);
        //Log.Info("Allarme");
        {
            var eventArguments = eventType.EventArguments;
            var eventId = (ByteString)eventArguments.GetFieldValue(args, "EventId");
            var eventIdString = eventId.ToString();

            if (!receivedEvents.ContainsKey(eventIdString))
            {
                var eventList = new List<EventData>
            {
                new EventData(eventNotifier, eventType, args)
            };
                receivedEvents.Add(eventIdString, eventList);
            }
            else
            {
                receivedEvents[eventIdString].Add(new EventData(eventNotifier, eventType, args));
            }

            if (receivedEvents[eventIdString].Count == Project.Current.Locales.Length)
            {
                try
                {
                    var alarmMessage = ConstructAlarmMessage(eventId);
                    PublishMessage(alarmMessage);
                }
                catch (Exception e)
                {
                    Log.Error(e.Message);
                }
            }
        }
    }

    private string ConstructAlarmMessage(ByteString eventId)
    {
        var currentEventList = receivedEvents[eventId.ToString()];
        var eventArguments = currentEventList[0].EventType.EventArguments;
        var args = currentEventList[0].Args;

        var timestamp = eventArguments.GetFieldValue(args, "Time");
        var ackedState = eventArguments.GetFieldValue(args, "AckedState/Id");
        var confirmedState = eventArguments.GetFieldValue(args, "ConfirmedState/Id");
        var activeState = eventArguments.GetFieldValue(args, "ActiveState/Id");
        var enabledState = eventArguments.GetFieldValue(args, "EnabledState/Id");
        var conditionName = eventArguments.GetFieldValue(args, "ConditionName");
        var sourceName = mqttClientUniqueName;
        var severity = eventArguments.GetFieldValue(args, "Severity");
        var localTime = eventArguments.GetFieldValue(args, "LocalTime");

        var sb = new StringBuilder();
        var sw = new StringWriter(sb);
        using (var writer = new JsonTextWriter(sw))
        {
            writer.Formatting = Formatting.None;

            writer.WriteStartObject();
            writer.WritePropertyName("ConditionName");
            writer.WriteValue(conditionName);
            writer.WritePropertyName("Time");
            writer.WriteValue(timestamp);
            writer.WritePropertyName("ActiveState_Id");
            writer.WriteValue(activeState);
            writer.WritePropertyName("AckedState_Id");
            writer.WriteValue(ackedState);
            writer.WritePropertyName("ConfirmedState_Id");
            writer.WriteValue(confirmedState);
            writer.WritePropertyName("EnabledState_Id");
            writer.WriteValue(enabledState);
            writer.WritePropertyName("SourceName");
            writer.WriteValue(sourceName);
            writer.WritePropertyName("Severity");
            writer.WriteValue(severity);
            writer.WritePropertyName("LocalTime");
            writer.WriteValue(((Struct)localTime).Values[0]);

            foreach (var evt in currentEventList)
            {
                var message = (LocalizedText)evt.EventType.EventArguments.GetFieldValue(evt.Args, "Message");
                writer.WritePropertyName("Message_" + message.LocaleId);
                writer.WriteValue(message.Text);
            }

            writer.WriteEnd();
        }

        return sb.ToString();
    }


    //private string ConstructAlarmMessage(IEventArguments eventArguments, IReadOnlyList<object> args, AlarmController alarmcontroller)
    //{
    //    var time = (DateTime)eventArguments.GetFieldValue(args, "Time");
    //    var localTime = eventArguments.GetFieldValue(args, "LocalTime");
    //    var conditionName = eventArguments.GetFieldValue(args, "ConditionName");
    //    var ackedState = eventArguments.GetFieldValue(args, "AckedState/Id");
    //    var confirmedState = eventArguments.GetFieldValue(args, "ConfirmedState/Id");
    //    var activeState = eventArguments.GetFieldValue(args, "ActiveState/Id");
    //    var enabledState = eventArguments.GetFieldValue(args, "EnabledState/Id");

    //    return "Alarm: " + conditionName + " ActiveState: " + activeState + " Time: " + time.AddMinutes(Int32.Parse(((object[])((UAManagedCore.Struct)localTime).Values)[0].ToString()));

    //}

    private bool GetInitialActiveState()
    {
        var retainedAlarmsNode = InformationModel.Get(FTOptix.Alarm.Objects.RetainedAlarms);

        var retainedAlarm = retainedAlarmsNode.Find(alarmObject.BrowseName);
        if (retainedAlarm == null)
            return false;

        return retainedAlarm.GetVariable("ActiveState/Id").Value;
    }

    private void InitMqttClient()
    {
        brokerIpAddress = (String)LogicObject.GetVariable("BrokerIpAddress").Value;
        mqttClientUniqueName = (String)LogicObject.GetVariable("MqttAlarmClientUniqueName").Value;
        mqttClientUniqueName = mqttClientUniqueName + GetRandomString(10);
        mqttLiveTopic = (string)LogicObject.GetVariable("MqttAlarmLiveTopic").Value;
        // Create a client connecting to the broker (default port is 1883)
        publishClient = new MqttClient(brokerIpAddress);
        // Connect to the broker
        publishClient.Connect(mqttClientUniqueName);
        Log.Info("MQTT Client Id: " + mqttClientUniqueName);
    }

    [ExportMethod]
    public void PublishMessage(string message)
    {
        
        publishClient.Publish(mqttLiveTopic,
                               System.Text.Encoding.UTF8.GetBytes(message),
                               MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE,
                               false);

        //// Publish a message
        //ushort msgId = publishClient.Publish((string)mqttLiveTopic.Value, // topic
        //    System.Text.Encoding.UTF8.GetBytes(message), // message body
        //    MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE, // QoS level
        //    false); // retained
    }

    public static string GetRandomString(int length)
    {
        var r = new Random();
        return new String(Enumerable.Range(0, length).Select(n => (Char)(r.Next(32, 127))).ToArray());
    }

    class EventData
    {
        public EventData(IUAObject eventNotifier, IUAObjectType eventType, IReadOnlyList<object> args)
        {
            EventNotifier = eventNotifier;
            EventType = eventType;
            Args = args;
        }

        public IUAObject EventNotifier { get; set; }
        public IUAObjectType EventType { get; set; }
        public IReadOnlyList<object> Args { get; set; }
    }

    private bool previousActiveState;
    private string brokerIpAddress = "";
    private string mqttClientUniqueName = "";
    private string mqttLiveTopic = "";
    private IEventRegistration eventRegistration;
    private Dictionary<string, List<EventData>> receivedEvents = new Dictionary<string, List<EventData>>();
    private IUANode emailUserNode;
    private IUAObject alarmObject;
    private MqttClient publishClient;

}
