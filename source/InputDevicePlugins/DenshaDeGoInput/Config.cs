//Simplified BSD License (BSD-2-Clause)
//
//Copyright (c) 2020, Marc Riera, The OpenBVE Project
//
//Redistribution and use in source and binary forms, with or without
//modification, are permitted provided that the following conditions are met:
//
//1. Redistributions of source code must retain the above copyright notice, this
//   list of conditions and the following disclaimer.
//2. Redistributions in binary form must reproduce the above copyright notice,
//   this list of conditions and the following disclaimer in the documentation
//   and/or other materials provided with the distribution.
//
//THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
//ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
//WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
//DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
//ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
//(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
//LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
//ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
//(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
//SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System.Drawing;
using OpenTK.Input;
using OpenBveApi.Interface;

namespace DenshaDeGoInput
{
	public partial class Config : Form
	{
		/// <summary>
		/// Internal list of connected controllers.
		/// </summary>
		private List<Guid> controllerList = new List<Guid>();

		public Config()
		{
			InitializeComponent();

			// Load language files
			Translations.LoadLanguageFiles(OpenBveApi.Path.CombineDirectory(DenshaDeGoInput.FileSystem.DataFolder, "Languages"));

			// Populate command boxes
			buttonselectBox.Items.Add(Translations.GetInterfaceString("denshadego_command_none"));
			buttonstartBox.Items.Add(Translations.GetInterfaceString("denshadego_command_none"));
			buttonaBox.Items.Add(Translations.GetInterfaceString("denshadego_command_none"));
			buttonbBox.Items.Add(Translations.GetInterfaceString("denshadego_command_none"));
			buttoncBox.Items.Add(Translations.GetInterfaceString("denshadego_command_none"));
			buttondBox.Items.Add(Translations.GetInterfaceString("denshadego_command_none"));
			buttonupBox.Items.Add(Translations.GetInterfaceString("denshadego_command_none"));
			buttondownBox.Items.Add(Translations.GetInterfaceString("denshadego_command_none"));
			buttonleftBox.Items.Add(Translations.GetInterfaceString("denshadego_command_none"));
			buttonrightBox.Items.Add(Translations.GetInterfaceString("denshadego_command_none"));
			buttonpedalBox.Items.Add(Translations.GetInterfaceString("denshadego_command_none"));
			buttonldoorBox.Items.Add(Translations.GetInterfaceString("denshadego_command_none"));
			buttonrdoorBox.Items.Add(Translations.GetInterfaceString("denshadego_command_none"));
			Translations.CommandInfo[] commands = Translations.CommandInfos.OrderBy(o => o.Command).ToArray();
			for (int i = 0; i < Translations.CommandInfos.Length; i++)
			{
				buttonselectBox.Items.Add(commands[i].Name);
				buttonstartBox.Items.Add(commands[i].Name);
				buttonaBox.Items.Add(commands[i].Name);
				buttonbBox.Items.Add(commands[i].Name);
				buttoncBox.Items.Add(commands[i].Name);
				buttondBox.Items.Add(commands[i].Name);
				buttonupBox.Items.Add(commands[i].Name);
				buttondownBox.Items.Add(commands[i].Name);
				buttonleftBox.Items.Add(commands[i].Name);
				buttonrightBox.Items.Add(commands[i].Name);
				buttonpedalBox.Items.Add(commands[i].Name);
				buttonldoorBox.Items.Add(commands[i].Name);
				buttonrdoorBox.Items.Add(commands[i].Name);
			}
		}

		/// <summary>
		/// Adds the available controllers to the device dropdown list.
		/// </summary>
		private void ListControllers()
		{
			// Clear the internal and visible lists
			controllerList.Clear();
			deviceBox.Items.Clear();

			controllerList.AddRange(InputTranslator.ConnectedControllers.Keys);

			foreach (Guid guid in controllerList)
			{
				deviceBox.Items.Add(InputTranslator.GetControllerName(guid));
			}

			// Adjust the width of the device dropdown to prevent truncation
			deviceBox.DropDownWidth = deviceBox.Width;
			foreach (var item in deviceBox.Items)
			{
				int currentItemWidth = (int)deviceBox.CreateGraphics().MeasureString(item.ToString(), deviceBox.Font).Width;
				if (currentItemWidth > deviceBox.DropDownWidth)
				{
					deviceBox.DropDownWidth = currentItemWidth;
				}
			}
		}

		/// <summary>
		/// Updates the interface to reflect the input from the controller.
		/// </summary>
		private void UpdateInterface()
		{
			if (InputTranslator.IsControllerConnected)
			{
				switch (InputTranslator.BrakeNotch)
				{
					case InputTranslator.BrakeNotches.Released:
						label_brake.Text = Translations.GetInterfaceString("denshadego_label_brake").Replace("[notch]", Translations.QuickReferences.HandleBrakeNull);
						break;
					case InputTranslator.BrakeNotches.Emergency:
						label_brake.Text = Translations.GetInterfaceString("denshadego_label_brake").Replace("[notch]", Translations.QuickReferences.HandleEmergency);
						break;
					default:
						label_brake.Text = Translations.GetInterfaceString("denshadego_label_brake").Replace("[notch]", Translations.QuickReferences.HandleBrake + (int)InputTranslator.BrakeNotch);
						break;
				}

				switch (InputTranslator.PowerNotch)
				{
					case InputTranslator.PowerNotches.N:
						label_power.Text = Translations.GetInterfaceString("denshadego_label_power").Replace("[notch]", Translations.QuickReferences.HandlePowerNull);
						break;
					default:
						label_power.Text = Translations.GetInterfaceString("denshadego_label_power").Replace("[notch]", Translations.QuickReferences.HandlePower + (int)InputTranslator.PowerNotch);
						break;
				}

				label_select.ForeColor = InputTranslator.ControllerButtons[(int)InputTranslator.ControllerButton.Select] == OpenTK.Input.ButtonState.Pressed ? Color.White : Color.Black;
				label_start.ForeColor = InputTranslator.ControllerButtons[(int)InputTranslator.ControllerButton.Start] == OpenTK.Input.ButtonState.Pressed ? Color.White : Color.Black;
				label_a.ForeColor = InputTranslator.ControllerButtons[(int)InputTranslator.ControllerButton.A] == OpenTK.Input.ButtonState.Pressed ? Color.White : Color.Black;
				label_b.ForeColor = InputTranslator.ControllerButtons[(int)InputTranslator.ControllerButton.B] == OpenTK.Input.ButtonState.Pressed ? Color.White : Color.Black;
				label_c.ForeColor = InputTranslator.ControllerButtons[(int)InputTranslator.ControllerButton.C] == OpenTK.Input.ButtonState.Pressed ? Color.White : Color.Black;
				label_d.ForeColor = InputTranslator.ControllerButtons[(int)InputTranslator.ControllerButton.D] == OpenTK.Input.ButtonState.Pressed ? Color.White : Color.Black;
				label_up.ForeColor = InputTranslator.ControllerButtons[(int)InputTranslator.ControllerButton.Up] == OpenTK.Input.ButtonState.Pressed ? Color.White : Color.Black;
				label_down.ForeColor = InputTranslator.ControllerButtons[(int)InputTranslator.ControllerButton.Down] == OpenTK.Input.ButtonState.Pressed ? Color.White : Color.Black;
				label_left.ForeColor = InputTranslator.ControllerButtons[(int)InputTranslator.ControllerButton.Left] == OpenTK.Input.ButtonState.Pressed ? Color.White : Color.Black;
				label_right.ForeColor = InputTranslator.ControllerButtons[(int)InputTranslator.ControllerButton.Right] == OpenTK.Input.ButtonState.Pressed ? Color.White : Color.Black;
				label_pedal.ForeColor = InputTranslator.ControllerButtons[(int)InputTranslator.ControllerButton.Pedal] == OpenTK.Input.ButtonState.Pressed ? Color.White : Color.Black;
				label_ldoor.ForeColor = InputTranslator.ControllerButtons[(int)InputTranslator.ControllerButton.LDoor] == OpenTK.Input.ButtonState.Pressed ? Color.White : Color.Black;
				label_rdoor.ForeColor = InputTranslator.ControllerButtons[(int)InputTranslator.ControllerButton.RDoor] == OpenTK.Input.ButtonState.Pressed ? Color.White : Color.Black;
			}

			switch (InputTranslator.ControllerModel)
			{
				case InputTranslator.ControllerModels.Classic:
					buttonCalibrate.Visible = true;
					label_d.Visible = false;
					label_up.Visible = false;
					label_down.Visible = false;
					label_left.Visible = false;
					label_right.Visible = false;
					label_pedal.Visible = false;
					label_ldoor.Visible = false;
					label_rdoor.Visible = false;
					break;
				case InputTranslator.ControllerModels.UnbalanceStandard:
					buttonCalibrate.Visible = false;
					label_d.Visible = true;
					label_pedal.Visible = false;
					label_ldoor.Visible = false;
					label_rdoor.Visible = false;
					if (ControllerUnbalance.HasDirectionButtons)
					{
						label_up.Visible = true;
						label_down.Visible = true;
						label_left.Visible = true;
						label_right.Visible = true;
					}
					else
					{
						label_up.Visible = false;
						label_down.Visible = false;
						label_left.Visible = false;
						label_right.Visible = false;
					}
					break;
				case InputTranslator.ControllerModels.UnbalanceRyojouhen:
					buttonCalibrate.Visible = false;
					label_d.Visible = true;
					label_pedal.Visible = false;
					label_ldoor.Visible = true;
					label_rdoor.Visible = true;
					label_up.Visible = true;
					label_down.Visible = true;
					label_left.Visible = true;
					label_right.Visible = true;
					break;
				case InputTranslator.ControllerModels.Ps2Type2:
				case InputTranslator.ControllerModels.Ps2Shinkansen:
					buttonCalibrate.Visible = false;
					label_d.Visible = true;
					label_pedal.Visible = true;
					label_ldoor.Visible = false;
					label_rdoor.Visible = false;
					label_up.Visible = true;
					label_down.Visible = true;
					label_left.Visible = true;
					label_right.Visible = true;
					break;
				case InputTranslator.ControllerModels.Ps2Ryojouhen:
					buttonCalibrate.Visible = false;
					label_d.Visible = true;
					label_pedal.Visible = true;
					label_ldoor.Visible = true;
					label_rdoor.Visible = true;
					label_up.Visible = true;
					label_down.Visible = true;
					label_left.Visible = true;
					label_right.Visible = true;
					break;
				default:
					buttonCalibrate.Visible = false;
					label_d.Visible = true;
					label_up.Visible = true;
					label_down.Visible = true;
					label_left.Visible = true;
					label_right.Visible = true;
					label_pedal.Visible = true;
					label_ldoor.Visible = true;
					label_rdoor.Visible = true;
					break;
			}
		}

		/// <summary>
		/// Retranslates the configutarion interface.
		/// </summary>
		private void UpdateTranslation()
		{
			Text = Translations.GetInterfaceString("denshadego_config_title");
			deviceInputBox.Text = Translations.GetInterfaceString("denshadego_input_section");
			buttonCalibrate.Text = Translations.GetInterfaceString("denshadego_calibration_button");
			label_device.Text = Translations.GetInterfaceString("denshadego_device");
			buttonMappingBox.Text = Translations.GetInterfaceString("denshadego_button_section");
			handleMappingBox.Text = Translations.GetInterfaceString("denshadego_handle_section");
			convertnotchesCheck.Text = Translations.GetInterfaceString("denshadego_option_convert");
			minmaxCheck.Text = Translations.GetInterfaceString("denshadego_option_keep_minmax");
			holdbrakeCheck.Text = Translations.GetInterfaceString("denshadego_option_holdbrake");
			buttonSave.Text = Translations.GetInterfaceString("denshadego_save_button");
			buttonCancel.Text = Translations.GetInterfaceString("denshadego_cancel_button");

			buttonselectBox.Items[0] = Translations.GetInterfaceString("denshadego_command_none");
			buttonstartBox.Items[0] = Translations.GetInterfaceString("denshadego_command_none");
			buttonaBox.Items[0] = Translations.GetInterfaceString("denshadego_command_none");
			buttonbBox.Items[0] = Translations.GetInterfaceString("denshadego_command_none");
			buttoncBox.Items[0] = Translations.GetInterfaceString("denshadego_command_none");
			buttondBox.Items[0] = Translations.GetInterfaceString("denshadego_command_none");
			buttonupBox.Items[0] = Translations.GetInterfaceString("denshadego_command_none");
			buttondownBox.Items[0] = Translations.GetInterfaceString("denshadego_command_none");
			buttonleftBox.Items[0] = Translations.GetInterfaceString("denshadego_command_none");
			buttonrightBox.Items[0] = Translations.GetInterfaceString("denshadego_command_none");
			buttonpedalBox.Items[0] = Translations.GetInterfaceString("denshadego_command_none");
			buttonldoorBox.Items[0] = Translations.GetInterfaceString("denshadego_command_none");
			buttonldoorBox.Items[0] = Translations.GetInterfaceString("denshadego_command_none");

			label_up.Text = Translations.GetInterfaceString("denshadego_label_up");
			label_down.Text = Translations.GetInterfaceString("denshadego_label_down");
			label_left.Text = Translations.GetInterfaceString("denshadego_label_left");
			label_right.Text = Translations.GetInterfaceString("denshadego_label_right");
			label_pedal.Text = Translations.GetInterfaceString("denshadego_label_pedal");
			label_ldoor.Text = Translations.GetInterfaceString("denshadego_label_ldoor");
			label_ldoor.Text = Translations.GetInterfaceString("denshadego_label_rdoor");
			label_buttonup.Text = Translations.GetInterfaceString("denshadego_label_up");
			label_buttondown.Text = Translations.GetInterfaceString("denshadego_label_down");
			label_buttonleft.Text = Translations.GetInterfaceString("denshadego_label_left");
			label_buttonright.Text = Translations.GetInterfaceString("denshadego_label_right");
			label_buttonpedal.Text = Translations.GetInterfaceString("denshadego_label_pedal");
			label_buttonldoor.Text = Translations.GetInterfaceString("denshadego_label_ldoor");
			label_buttonrdoor.Text = Translations.GetInterfaceString("denshadego_label_rdoor");
		}

		private void Config_Shown(object sender, EventArgs e)
		{
			// Add connected devices to device list
			InputTranslator.Update();
			ListControllers();

			// Try to select the current device
			if (controllerList.Contains(InputTranslator.ActiveControllerGuid))
			{
				deviceBox.SelectedIndex = controllerList.IndexOf(InputTranslator.ActiveControllerGuid);
			}

			// Set command boxes
			buttonselectBox.SelectedIndex = DenshaDeGoInput.ButtonProperties[0].Command;
			buttonstartBox.SelectedIndex = DenshaDeGoInput.ButtonProperties[1].Command;
			buttonaBox.SelectedIndex = DenshaDeGoInput.ButtonProperties[2].Command;
			buttonbBox.SelectedIndex = DenshaDeGoInput.ButtonProperties[3].Command;
			buttoncBox.SelectedIndex = DenshaDeGoInput.ButtonProperties[4].Command;
			buttondBox.SelectedIndex = DenshaDeGoInput.ButtonProperties[5].Command;
			buttonupBox.SelectedIndex = DenshaDeGoInput.ButtonProperties[6].Command;
			buttondownBox.SelectedIndex = DenshaDeGoInput.ButtonProperties[7].Command;
			buttonleftBox.SelectedIndex = DenshaDeGoInput.ButtonProperties[8].Command;
			buttonrightBox.SelectedIndex = DenshaDeGoInput.ButtonProperties[9].Command;
			buttonpedalBox.SelectedIndex = DenshaDeGoInput.ButtonProperties[10].Command;
			buttonldoorBox.SelectedIndex = DenshaDeGoInput.ButtonProperties[11].Command;
			buttonrdoorBox.SelectedIndex = DenshaDeGoInput.ButtonProperties[12].Command;


			// Set checkboxes
			convertnotchesCheck.Checked = DenshaDeGoInput.ConvertNotches;
			minmaxCheck.Checked = DenshaDeGoInput.KeepMaxMin;
			holdbrakeCheck.Checked = DenshaDeGoInput.MapHoldBrake;
			minmaxCheck.Enabled = DenshaDeGoInput.ConvertNotches;

			// Start timer
			timer1.Enabled = true;

			// Translate the interface to the current language
			UpdateTranslation();
		}

		private void Config_FormClosed(Object sender, FormClosedEventArgs e)
		{
			// Reload the previous config and close the config dialog
			DenshaDeGoInput.LoadConfig();
			ControllerPs2.ControllerDisplayEnabled = false;
		}

		private void deviceBox_SelectedIndexChanged(object sender, EventArgs e)
		{
			InputTranslator.ControllerModels model = InputTranslator.ConnectedModels[controllerList[deviceBox.SelectedIndex]];
			if (model == InputTranslator.ControllerModels.Ps2Type2 || model == InputTranslator.ControllerModels.Ps2Shinkansen)
			{
				ControllerPs2.ControllerDisplayEnabled = true;
			}
			else
			{
				ControllerPs2.ControllerDisplayEnabled = false;
			}
			InputTranslator.Update();
			InputTranslator.ActiveControllerGuid = controllerList[deviceBox.SelectedIndex];
		}

		private void buttonselectBox_SelectedIndexChanged(object sender, EventArgs e)
		{
			DenshaDeGoInput.ButtonProperties[(int)InputTranslator.ControllerButton.Select].Command = buttonselectBox.SelectedIndex;
		}

		private void buttonstartBox_SelectedIndexChanged(object sender, EventArgs e)
		{
			DenshaDeGoInput.ButtonProperties[(int)InputTranslator.ControllerButton.Start].Command = buttonstartBox.SelectedIndex;
		}

		private void buttonaBox_SelectedIndexChanged(object sender, EventArgs e)
		{
			DenshaDeGoInput.ButtonProperties[(int)InputTranslator.ControllerButton.A].Command = buttonaBox.SelectedIndex;
		}

		private void buttonbBox_SelectedIndexChanged(object sender, EventArgs e)
		{
			DenshaDeGoInput.ButtonProperties[(int)InputTranslator.ControllerButton.B].Command = buttonbBox.SelectedIndex;
		}

		private void buttoncBox_SelectedIndexChanged(object sender, EventArgs e)
		{
			DenshaDeGoInput.ButtonProperties[(int)InputTranslator.ControllerButton.C].Command = buttoncBox.SelectedIndex;
		}

		private void buttondBox_SelectedIndexChanged(object sender, EventArgs e)
		{
			DenshaDeGoInput.ButtonProperties[(int)InputTranslator.ControllerButton.D].Command = buttondBox.SelectedIndex;
		}

		private void buttonupBox_SelectedIndexChanged(object sender, EventArgs e)
		{
			DenshaDeGoInput.ButtonProperties[(int)InputTranslator.ControllerButton.Up].Command = buttonupBox.SelectedIndex;
		}

		private void buttondownBox_SelectedIndexChanged(object sender, EventArgs e)
		{
			DenshaDeGoInput.ButtonProperties[(int)InputTranslator.ControllerButton.Down].Command = buttondownBox.SelectedIndex;
		}

		private void buttonleftBox_SelectedIndexChanged(object sender, EventArgs e)
		{
			DenshaDeGoInput.ButtonProperties[(int)InputTranslator.ControllerButton.Left].Command = buttonleftBox.SelectedIndex;
		}

		private void buttonrightBox_SelectedIndexChanged(object sender, EventArgs e)
		{
			DenshaDeGoInput.ButtonProperties[(int)InputTranslator.ControllerButton.Right].Command = buttonrightBox.SelectedIndex;
		}

		private void buttonpedalBox_SelectedIndexChanged(object sender, EventArgs e)
		{
			DenshaDeGoInput.ButtonProperties[(int)InputTranslator.ControllerButton.Pedal].Command = buttonpedalBox.SelectedIndex;
		}
		private void buttonldoorBox_SelectedIndexChanged(object sender, EventArgs e)
		{
			DenshaDeGoInput.ButtonProperties[(int)InputTranslator.ControllerButton.LDoor].Command = buttonldoorBox.SelectedIndex;
		}
		private void buttonrdoorBox_SelectedIndexChanged(object sender, EventArgs e)
		{
			DenshaDeGoInput.ButtonProperties[(int)InputTranslator.ControllerButton.RDoor].Command = buttonrdoorBox.SelectedIndex;
		}

		private void convertnotchesCheck_CheckedChanged(object sender, EventArgs e)
		{
			DenshaDeGoInput.ConvertNotches = convertnotchesCheck.Checked;
			minmaxCheck.Enabled = DenshaDeGoInput.ConvertNotches;
		}

		private void minmaxCheck_CheckedChanged(object sender, EventArgs e)
		{
			DenshaDeGoInput.KeepMaxMin = minmaxCheck.Checked;
		}

		private void holdbrakeCheck_CheckedChanged(object sender, EventArgs e)
		{
			DenshaDeGoInput.MapHoldBrake = holdbrakeCheck.Checked;
		}

		private void buttonCalibrate_Click(object sender, EventArgs e)
		{
			timer1.Stop();
			ControllerClassic.Calibrate();
			timer1.Start();
		}

		private void buttonSave_Click(object sender, EventArgs e)
		{
			// Save the config and close the config dialog
			DenshaDeGoInput.SaveConfig();
			Close();
		}

		private void buttonCancel_Click(object sender, EventArgs e)
		{
			// Reload the previous config and close the config dialog
			DenshaDeGoInput.LoadConfig();
			Close();
		}

		private void timer1_Tick(object sender, EventArgs e)
		{
			InputTranslator.Update();
			UpdateInterface();
		}

	}
}
