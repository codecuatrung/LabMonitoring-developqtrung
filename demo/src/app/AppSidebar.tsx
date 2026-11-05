import { Avatar, Button, Dropdown, Input, Layout, Progress } from "antd";
import {
  AppstoreOutlined,
  CalendarOutlined,
  CloseOutlined,
  ClockCircleOutlined,
  DatabaseOutlined,
  DownOutlined,
  ReloadOutlined,
  SafetyCertificateOutlined,
  SearchOutlined,
  TeamOutlined,
  UserOutlined,
} from "@ant-design/icons";
import { useState } from "react";
import type { NavigationItem, Screen } from "./model";

export type AppSidebarProps = {
  role: string;
  selfName: string;
  screen: Screen;
  visibleNavigation: NavigationItem[];
  recordsUsed: number;
  dutyBadgeCount: number;
  dutyBadgeLabel: string;
  isMobile: boolean;
  mobileOpen: boolean;
  onScreenChange: (screen: Screen) => void;
  onToggle: () => void;
  onCloseMobile: () => void;
  onResetDemo: () => void;
};

export function AppSidebar({
  role,
  selfName,
  screen,
  visibleNavigation,
  recordsUsed,
  dutyBadgeCount,
  dutyBadgeLabel,
  isMobile,
  mobileOpen,
  onScreenChange,
  onToggle,
  onCloseMobile,
  onResetDemo,
}: AppSidebarProps) {
  const [query, setQuery] = useState("");
  const [expandedGroups, setExpandedGroups] = useState<string[]>([
    "teaching",
    "duties",
    "catalog",
    "management",
  ]);
  const groups = [
    {
      key: "teaching",
      label: "Giảng dạy",
      icon: <CalendarOutlined />,
      screens: ["schedule", "myCalendar"] as Screen[],
    },
    {
      key: "duties",
      label: "Ca trực",
      icon: <ClockCircleOutlined />,
      screens: ["duties", "templates"] as Screen[],
    },
    {
      key: "catalog",
      label: "Danh mục",
      icon: <TeamOutlined />,
      screens: ["people", "rooms"] as Screen[],
    },
    {
      key: "management",
      label: "Thống kê & quản trị",
      icon: <SafetyCertificateOutlined />,
      screens: ["reports", "admin"] as Screen[],
    },
  ];
  const normalizedQuery = query.trim().toLocaleLowerCase("vi");
  const matches = (item: NavigationItem) =>
    `${item.label} ${item.description}`
      .toLocaleLowerCase("vi")
      .includes(normalizedQuery);
  const overview = visibleNavigation.find((item) => item.key === "overview");
  const isExpanded = (groupKey: string) =>
    normalizedQuery.length > 0 || expandedGroups.includes(groupKey);
  const toggleGroup = (groupKey: string) =>
    setExpandedGroups((current) =>
      current.includes(groupKey)
        ? current.filter((key) => key !== groupKey)
        : [...current, groupKey],
    );
  const recordCapacity = 1500;
  const usagePercent = Math.min(
    100,
    Math.round((recordsUsed / recordCapacity) * 100),
  );

  return (
    <>
      {mobileOpen && (
        <button
          type="button"
          className="nav-backdrop"
          aria-label="Đóng thanh điều hướng"
          onClick={onCloseMobile}
        />
      )}
      <Layout.Sider
        width={264}
        trigger={null}
        className={`sidebar ${mobileOpen ? "mobile-nav-open" : ""}`}
      >
        <div className="sidebar-inner">
          <div className="sidebar-top">
            <div className="brand">
              <span className="brand-mark" aria-hidden="true">
                <AppstoreOutlined />
              </span>
              <strong>LabMonitoring</strong>
              {isMobile && (
                <Button
                  className="sidebar-toggle"
                  type="text"
                  aria-label="Đóng menu"
                  title="Đóng menu"
                  icon={<CloseOutlined />}
                  onClick={onToggle}
                />
              )}
            </div>
            <div className="workspace-label">
              <span>KHÔNG GIAN LÀM VIỆC</span>
              <strong>{role}</strong>
            </div>
            <Input
              className="sidebar-search"
              prefix={<SearchOutlined />}
              allowClear
              value={query}
              onChange={(event) => setQuery(event.target.value)}
              placeholder="Tìm trang..."
              aria-label="Tìm trang điều hướng"
            />
          </div>

          <nav className="sidebar-nav" aria-label="Điều hướng chính">
            {overview && matches(overview) && (
              <button
                type="button"
                className={`sidebar-link sidebar-overview ${screen === "overview" ? "is-active" : ""}`}
                onClick={() => {
                  onScreenChange("overview");
                  onCloseMobile();
                }}
              >
                <AppstoreOutlined />
                <span>{overview.label}</span>
              </button>
            )}
            {groups.map((group) => {
              const items = visibleNavigation.filter((item) =>
                group.screens.includes(item.key),
              );
              const filteredItems = items.filter(
                (item) =>
                  !normalizedQuery ||
                  matches(item) ||
                  group.label.toLocaleLowerCase("vi").includes(normalizedQuery),
              );
              if (!filteredItems.length) return null;
              const active = items.some((item) => item.key === screen);
              return (
                <section className="sidebar-group" key={group.key}>
                  <button
                    type="button"
                    className={`sidebar-group-trigger ${active ? "is-current" : ""}`}
                    aria-expanded={isExpanded(group.key)}
                    onClick={() => toggleGroup(group.key)}
                  >
                    <span className="sidebar-group-name">
                      {group.icon}
                      <span>{group.label}</span>
                    </span>
                    <DownOutlined
                      className={`sidebar-chevron ${isExpanded(group.key) ? "is-expanded" : ""}`}
                    />
                  </button>
                  {isExpanded(group.key) && (
                    <div className="sidebar-submenu">
                      {filteredItems
                        .filter((item) => !normalizedQuery || matches(item) || group.label.toLocaleLowerCase("vi").includes(normalizedQuery))
                        .map((item) => (
                          <button
                            type="button"
                            key={item.key}
                            className={`sidebar-child-link ${screen === item.key ? "is-active" : ""}`}
                            onClick={() => {
                              onScreenChange(item.key);
                              onCloseMobile();
                            }}
                          >
                            <span>{item.label}</span>
                            {item.key === "duties" && dutyBadgeCount > 0 ? (
                              <span
                                className="sidebar-badge"
                                title={dutyBadgeLabel}
                                aria-label={dutyBadgeLabel}
                              >
                                {dutyBadgeCount}
                              </span>
                            ) : null}
                          </button>
                        ))}
                    </div>
                  )}
                </section>
              );
            })}
            {normalizedQuery && !visibleNavigation.some(matches) && (
              <div className="sidebar-empty">Không tìm thấy trang phù hợp</div>
            )}
          </nav>

          <div className="sidebar-bottom">
            <div className="storage-block">
              <div className="storage-heading">
                <span>Dữ liệu demo · RAM</span>
                <DatabaseOutlined />
              </div>
              <strong>{recordsUsed.toLocaleString("vi-VN")} / {recordCapacity.toLocaleString("vi-VN")} bản ghi</strong>
              <Progress percent={usagePercent} showInfo={false} size="small" />
              <Button
                block
                icon={<ReloadOutlined />}
                onClick={() => {
                  onScreenChange("admin");
                  onCloseMobile();
                }}
              >
                Quản lý dữ liệu
              </Button>
            </div>
            <div className="profile-row">
              <Avatar icon={<UserOutlined />} />
              <div>
                <strong>{selfName}</strong>
                <small>{role}</small>
              </div>
              <Dropdown
                menu={{
                  items: [
                    {
                      key: "reset",
                      label: "Khôi phục dữ liệu mẫu",
                      onClick: onResetDemo,
                    },
                  ],
                }}
              >
                <Button type="text" aria-label="Tài khoản">•••</Button>
              </Dropdown>
            </div>
          </div>
        </div>
      </Layout.Sider>
    </>
  );
}
