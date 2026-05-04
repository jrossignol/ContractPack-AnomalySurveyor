using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using UnityEngine;
using KSP;
using Contracts;
using ContractConfigurator;
using ContractConfigurator.Parameters;
using ContractConfigurator.Behaviour;

namespace AnomalySurveyor
{
    public class AnomalyScanParameter : ContractConfiguratorParameter, ParameterDelegateContainer
    {
        public bool ChildChanged { get; set; }

        bool vesselCheckRequired = true;
        string vesselName = null;
        Vessel vessel = null;
        double maxDistance;
        ModuleKerbNetAccess kerbnet;

        public AnomalyScanParameter()
        {
        }

        public AnomalyScanParameter(string vesselName)
        {
            this.vesselName = vesselName;
        }

        protected override string GetParameterTitle()
        {
            return "Anomaly Scan Parameter";
        }

        protected override void OnRegister()
        {
            ContractVesselTracker.OnVesselAssociation.Add(new EventData<GameEvents.HostTargetAction<Vessel, string>>.OnEvent(OnVesselAssociation));
            ContractVesselTracker.OnVesselDisassociation.Add(new EventData<GameEvents.HostTargetAction<Vessel, string>>.OnEvent(OnVesselAssociation));
        }

        protected override void OnUnregister()
        {
            ContractVesselTracker.OnVesselAssociation.Remove(new EventData<GameEvents.HostTargetAction<Vessel, string>>.OnEvent(OnVesselAssociation));
            ContractVesselTracker.OnVesselDisassociation.Remove(new EventData<GameEvents.HostTargetAction<Vessel, string>>.OnEvent(OnVesselAssociation));
        }

        protected void OnVesselAssociation(GameEvents.HostTargetAction<Vessel, string> hta)
        {
            if (string.Equals(hta.target, vesselName))
            {
                vesselCheckRequired = true;
            }
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();

            bool scanRequired = false;

            // Derive vessel details
            if (vesselCheckRequired)
            {
                LoggingUtil.LogVerbose(this, "Doing vessel check");
                vessel = ContractVesselTracker.Instance.GetAssociatedVessel(vesselName);
                kerbnet = null;

                // No vessel yet
                if (vessel == null)
                {
                    vesselCheckRequired = false;
                    return;
                }

                LoggingUtil.LogVerbose(this, "Scanning vessel is {0}", vessel.name);

                // Find the scanner
                foreach (ProtoPartSnapshot pps in vessel.protoVessel.protoPartSnapshots)
                {
                    foreach (ProtoPartModuleSnapshot ppms in pps.modules)
                    {
                        if (ppms.moduleName == "ModuleKerbNetAccess")
                        {
                            ConfigNode mod = ppms.moduleValues;

                            if (pmk.AnomalyDetection > 0)
                            {
                                LoggingUtil.LogVerbose(this, "Found potential kernet module on part {0}", pmk.part.partName);
                                if (kerbnet == null)
                                {
                                    kerbnet = pmk;
                                }
                                else
                                {
                                    if (pmk.MaximumFoV > kerbnet.MaximumFoV)
                                    {
                                        kerbnet = pmk;
                                    }
                                }
                            }
                        }
                    }
                }

                // Only proceed if we found a suitable Kerbnet module
                if (kerbnet == null)
                {
                    vessel = null;
                }
                else
                {
                    LoggingUtil.LogVerbose(this, "Kerbnet part is {0}", kerbnet.part.partName);
                    maxDistance = 10000.0 / Math.Tan(kerbnet.MinimumFoV / 2.0);
                    LoggingUtil.LogVerbose(this, "Maximum distance for vessel {0} calculated as {1}", vessel, maxDistance);
                }

                vesselCheckRequired = false;
                scanRequired = (vessel != null);
            }
            // Check if new scan is required
            else if (vessel != null)
            {
                // Do checks
                scanRequired = true;
            }

            // Do scan
            if (scanRequired)
            {
                scanRequired = false;

            }

        }
        protected override void OnParameterSave(ConfigNode node)
        {
            if (!string.IsNullOrEmpty(vesselName))
            {
                node.AddValue("vessel", vesselName);
            }
        }

        protected override void OnParameterLoad(ConfigNode node)
        {
            try
            {
                vesselName = ConfigNodeUtil.ParseValue<string>(node, "vessel", null);

                // Create the parameter delegate for the vessel list
                //CreateVesselListParameter();
            }
            finally
            {
                ParameterDelegate<Vessel>.OnDelegateContainerLoad(node);
            }
        }
    }
}
