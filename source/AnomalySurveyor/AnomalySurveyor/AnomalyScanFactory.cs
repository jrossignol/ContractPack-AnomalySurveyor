using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KSP;
using Contracts;
using Contracts.Parameters;
using ContractConfigurator;

namespace AnomalySurveyor
{
    class AnomalyScanFactory : ParameterFactory
    {
        string vessel = null;

        public override bool Load(ConfigNode configNode)
        {
            // Load base class
            bool valid = base.Load(configNode);

            valid &= ConfigNodeUtil.ParseValue<string>(configNode, "vessel", x => vessel = x, this, (string)null);

            return valid;
        }

        public override ContractParameter Generate(Contract contract)
        {
            return new AnomalyScanParameter(vessel);
        }
    }
}
