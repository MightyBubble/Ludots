using Sango.Core;
using Sango.Core.Player;

namespace Sango.Render
{
    public class CityPersonSearchingEvent : RenderEventBase
    {
        public City city;
        public Person person;
        public Person target;
        public int searchingType = 0; // 1是工作制

        public void Init(City city, Person person)
        {
            this.city = city;
            this.person = person;
            this.target = null;
            searchingType = 0;
            IsDone = false;
        }
        public override void Enter(Scenario scenario)
        {
            int rs = city.DoJobSearching(person, out target);
            // M3.e:玩家侧搜索结果对话框改由 ScenarioEvent 表驱动(Data/ScenarioEvent/
            // 10_搜索失败 / 11_搜索到人才;表 formatContent 与本处原硬编码串逐字同源,
            // 变量面 {:ActionPerson}=执行搜索武将、{:TargetPerson}=发现的人才)。
            // 表文本随对话框镜像入玩家消息流(headless/Web 消息流的自然承载面)。
            if (rs < 0)
            {
                if (city.mBelongCorps.IsPlayerControl && searchingType == 0)
                {
                    string content = ScenarioEventContent(10, 0);
                    PlayerMessage.AddTextMessage(content, city.mBelongForce, city.x, city.y);
                    GameDialog.Instance.Open(GameDialog.DialogStyle.ClickPersonSay, content, () =>
                    {
                        IsDone = true;
                    }, person);
                }
                else
                {
                    IsDone = true;
                }
                return;
            }

            if (!city.mBelongCorps.IsPlayerControl)
            {
                if (rs == 0)
                {
                    person.JobRecruitPerson(target, (int)PersonRecruitType.OnSearching);
                }
                IsDone = true;
                return;
            }

            if (rs == 0)
            {
                string content = ScenarioEventContent(11, 0);
                GameDialog.Instance.Open(GameDialog.DialogStyle.ClickPersonSay, content, () =>
                {
                    //展示武将
                    GameSystem.GetSystem<PersonRecruit>().Start(person, target, 0, 1, x =>
                    {
                        if (x.result == 1)
                        {
                            GameDialog.Instance.Open(GameDialog.DialogStyle.ClickPersonSay, $"成功招募了{target.ColorName}", () =>
                            {
                                GameDialog.Instance.Open(GameDialog.DialogStyle.ClickPersonSay, $"{target.ColorName}愿为主公献犬马之劳", () =>
                                {
                                    IsDone = true;
                                }, target);
                            }, person);
                        }
                        else if (x.result == 0)
                        {
                            if (searchingType == 0)
                            {
                                GameDialog.Instance.Open(GameDialog.DialogStyle.ClickPersonSay, $"很遗憾，\n未能招募到{target.ColorName}", () =>
                                {
                                    IsDone = true;
                                }, person);
                            }
                            else
                            {
                                IsDone = true;
                            }
                        }
                        else
                            IsDone = true;
                    });
                }, person);
            }
            else
            {
                if (searchingType == 0)
                {
                    GameDialog.Instance.Open(GameDialog.DialogStyle.ClickPersonSay, $"发现了资金{rs}", () =>
                    {
                        IsDone = true;
                    }, person);
                }
                else
                {
                    IsDone = true;
                }
            }
        }

        /// <summary>ScenarioEvent 组条目文本(变量绑定:ActionPerson/TargetPerson/ActionCity/ActionForce)。</summary>
        string ScenarioEventContent(int groupId, int entryIndex)
        {
            ScenarioEventTableEntry entry = ScenerioEventManager.Instance.GetGroup(groupId)[entryIndex];
            return entry.FormatContent(new ScenarioEventData
            {
                ActionPerson = person,
                TargetPerson = target,
                ActionCity = city,
                ActionForce = city.mBelongForce,
            });
        }

        public override void Exit(Scenario scenario)
        {

        }

        public override bool IsVisible()
        {
            return city.mBelongCorps.IsPlayer;
        }

        public override bool Update(Scenario scenario, float deltaTime)
        {
            return IsDone;
        }
    }
}
