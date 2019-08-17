﻿using GBCLV3.Models;
using GBCLV3.Views;
using Stylet;

namespace GBCLV3.ViewModels.Pages
{
    class VersionsRootViewModel : Conductor<IScreen>.StackNavigation
    {
        #region Private Members

        //IoC
        private readonly VersionsManagementViewModel _versionsManagementVM;
        private readonly GameInstallViewModel _gameInstallVM;
        private readonly ForgeInstallViewModel _forgeInstallVM;

        #endregion

        #region Constructor

        public VersionsRootViewModel(
            VersionsManagementViewModel versionsManagementVM,
            GameInstallViewModel gameInstallVM,
            ForgeInstallViewModel forgeInstallVM)
        {
            _versionsManagementVM = versionsManagementVM;
            _gameInstallVM = gameInstallVM;
            _forgeInstallVM = forgeInstallVM;

            this.ActivateItem(_versionsManagementVM);
            _versionsManagementVM.NavigateView += versionID =>
            {
                if (versionID == null) this.ActivateItem(_gameInstallVM);
                else
                {
                    _forgeInstallVM.GameVersion = versionID;
                    this.ActivateItem(_forgeInstallVM);
                }
            };
        }

        #endregion

        #region Bindings

        #endregion
    }
}
