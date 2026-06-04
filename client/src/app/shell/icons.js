import React from 'react';
import {
  LuBot,
  LuChartLine,
  LuCircleHelp,
  LuFolderOpen,
  LuHouse,
  LuUser,
} from 'react-icons/lu';

const iconMap = {
  admin: LuUser,
  analytics: LuChartLine,
  brand: LuBot,
  help: LuCircleHelp,
  home: LuHouse,
  workspaces: LuFolderOpen,
};

export function ShellIcon({ name, ...props }) {
  const Icon = iconMap[name] || LuCircleHelp;
  return <Icon aria-hidden="true" {...props} />;
}
